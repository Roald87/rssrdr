module SimpleRssServer.Cache

open Microsoft.Extensions.Logging
open System
open System.Text
open System.Text.Json
open System.Text.Json.Serialization

open SimpleRssServer.Config
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.MemoryCache

[<JsonConverter(typeof<FetchFailureKindConverter>)>]
[<Struct>]
type FetchFailureKind =
    | HttpError
    | Timeout

and private FetchFailureKindConverter() =
    inherit JsonConverter<FetchFailureKind>()

    override _.Read(reader, _, _) =
        match reader.GetString() with
        | "Timeout" -> Timeout
        | _ -> HttpError

    override _.Write(writer, value, _) =
        writer.WriteStringValue(
            match value with
            | HttpError -> "HttpError"
            | Timeout -> "Timeout"
        )

type FetchFailure =
    { LastFailure: DateTimeOffset
      ConsecutiveFailures: int
      Kind: FetchFailureKind }

let failureFilePath (cachePath: OsPath) = cachePath + ".failures"

let getBackoffHours failures =
    // Exponential backoff: 1hr, 2hrs, 4hrs, 8hrs, max 24hrs
    min 24.0 (Math.Pow(2.0, float (failures - 1)))

let getTimeoutBackoffMinutes failures =
    // 5min, 10min, 20min, ..., max 120min
    min 120.0 (5.0 * Math.Pow(2.0, float (failures - 1)))

let isCacheExpired (cacheConfig: CacheConfig) (modTime: DateTimeOffset) =
    (DateTimeOffset.Now - modTime) > cacheConfig.Expiration

// File.GetLastWriteTime returns this sentinel for a missing file, letting us check
// existence and last-write-time in a single filesystem stat instead of two.
let private missingFileTimestamp = DateTime.FromFileTime 0L

let fileLastModified (path: OsPath) =
    match OsFile.getLastWriteTime path with
    | t when t = missingFileTimestamp -> None
    | t -> Some(DateTimeOffset t)

let readCache (cachePath: OsPath) =
    if OsFile.exists cachePath then
        Some(OsFile.readAllText cachePath)
    else
        None

let createDirectoryForPath (path: OsPath) =
    let (OsPath dir) = OsPath.getDirectoryName path

    if not (String.IsNullOrEmpty dir) then
        OsDirectory.create (OsPath dir)

let writeCache cachePath (content: string) =
    createDirectoryForPath cachePath
    OsFile.writeAllText cachePath content

let clearFailure cachePath =
    let failurePath = failureFilePath cachePath

    if OsFile.exists failurePath then
        OsFile.delete failurePath

let readFailureRecord (logger: ILogger) cachePath : FetchFailure option =
    let failurePath = failureFilePath cachePath

    if OsFile.exists failurePath then
        try
            Some(JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath))
        with ex ->
            logger.LogWarning(ex, "Failed to read failure record at {Path}", failurePath)
            None
    else
        None

let recordFailureOfKind (logger: ILogger) cachePath (kind: FetchFailureKind) =
    let failurePath = failureFilePath cachePath
    createDirectoryForPath failurePath

    let failure =
        match readFailureRecord logger cachePath with
        | Some existing when existing.Kind = kind ->
            { existing with
                LastFailure = DateTimeOffset.Now
                ConsecutiveFailures = existing.ConsecutiveFailures + 1 }
        | _ ->
            { LastFailure = DateTimeOffset.Now
              ConsecutiveFailures = 1
              Kind = kind }

    OsFile.writeAllText failurePath (JsonSerializer.Serialize failure)

/// Classifies a fetch failure and records it, so callers just report the error
/// without needing to know about FetchFailureKind or backoff bookkeeping.
let recordFailure (logger: ILogger) cachePath (e: DomainError) =
    match e with
    | HttpRequestTimedOut _ -> recordFailureOfKind logger cachePath Timeout
    | _ -> recordFailureOfKind logger cachePath HttpError

let nextRetry (logger: ILogger) cachePath =
    readFailureRecord logger cachePath
    |> Option.map (fun failure ->
        match failure.Kind with
        | Timeout -> failure.LastFailure.AddMinutes(getTimeoutBackoffMinutes failure.ConsecutiveFailures)
        | HttpError -> failure.LastFailure.AddHours(getBackoffHours failure.ConsecutiveFailures))

let clearExpiredCache (logger: ILogger) (cacheDir: OsPath) (retention: TimeSpan) =
    if not (OsDirectory.exists cacheDir) then
        logger.LogWarning("Cache directory {Dir} does not exist", cacheDir)
    else
        cacheDir |> OsDirectory.deleteFilesOlderThan retention (fun _ -> true)

let private invalidFilenameCharsRegex =
    RegularExpressions.Regex("[.?=:/]+", RegularExpressions.RegexOptions.Compiled)

let convertUrlToValidFilename (uri: Uri) =
    invalidFilenameCharsRegex.Replace(uri.AbsoluteUri, "_") |> Filename

let cachePathFor (cacheConfig: CacheConfig) (uri: Uri) =
    OsPath.combine cacheConfig.Dir (convertUrlToValidFilename uri)

type BackoffState =
    | ReadyToFetch
    | InBackoffWithCache of waitTime: TimeSpan
    | InBackoffNoCache of waitTime: TimeSpan

/// Given whether a (possibly stale) cache file exists and the next allowed retry
/// time from the failure record, decide whether a fetch is currently permitted.
let computeBackoffState (cacheModified: DateTimeOffset option) (nextAttempt: DateTimeOffset option) =
    match nextAttempt with
    | Some na when na > DateTimeOffset.Now ->
        let waitTime = na - DateTimeOffset.Now

        match cacheModified with
        | Some _ -> InBackoffWithCache waitTime
        | None -> InBackoffNoCache waitTime
    | _ -> ReadyToFetch

let getCacheAge (logger: ILogger) (cacheConfig: CacheConfig) (url: Uri) =
    let cacheAge = cachePathFor cacheConfig url |> fileLastModified

    match cacheAge with
    | None ->
        logger.LogWarning(
            "No cache file found for {Url}, which is unexpected during a periodic update. Updating cache regardless.",
            url
        )

        Some(PendingFetch(None, url))
    | Some modTime when isCacheExpired cacheConfig modTime -> Some(PendingFetch(cacheAge, url))
    | _ -> None

/// Pipeline step: turn a PendingFetch that is still inside its backoff window into
/// a ProcessingError so the fetch stage never contacts a feed we should leave alone.
let applyBackoff (logger: ILogger) (cacheConfig: CacheConfig) (ups: UriProcessState) : UriProcessState =
    match ups with
    | PendingFetch(cacheModified, uri) ->
        let cachePath = cachePathFor cacheConfig uri

        match computeBackoffState cacheModified (nextRetry logger cachePath) with
        | ReadyToFetch -> ups
        | InBackoffWithCache waitTime -> ProcessingError(PreviousHttpRequestFailedButPageCached(uri, waitTime))
        | InBackoffNoCache waitTime -> ProcessingError(PreviousHttpRequestFailed(uri, waitTime))
    | _ -> ups

let readFromCache (cacheConfig: CacheConfig) (memCache: InMemoryCache) (ups: UriProcessState) : UriProcessState =
    match ups with
    | TryFetchFromCache u ->
        match memCache.TryGet(u.AbsoluteUri, cacheConfig.Expiration) with
        | Some articles -> FeedArticles articles
        | None ->
            let cachePath = cachePathFor cacheConfig u
            let cacheModified = fileLastModified cachePath

            match cacheModified with
            | None -> PendingFetch(None, u)
            | Some modTime when isCacheExpired cacheConfig modTime -> PendingFetch(Some modTime, u)
            | Some _ ->
                match readCache cachePath with
                | Some s -> UnparsedCachedContent(s, u)
                | None -> PendingFetch(None, u)
    | ProcessingError(DomainErrorUri feedUri as e) ->
        let cachePath = cachePathFor cacheConfig feedUri

        match readCache cachePath with
        | Some content -> UnparsedStaleCachedContent(content, feedUri, e)
        | None -> ProcessingError e
    | _ -> ups

let cacheSuccessfulFetch cacheConfig ups =
    match ups with
    | ParsedLiveFeed(xml, feed) ->
        let cachePath = cachePathFor cacheConfig (Uri feed.Link)
        writeCache cachePath xml.Value
    | _ -> ()

    ups
