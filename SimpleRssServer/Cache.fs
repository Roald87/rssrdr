module SimpleRssServer.Cache

open Microsoft.Extensions.Logging
open System
open System.IO
open System.Text
open System.Text.Json

open SimpleRssServer.Config
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.MemoryCache

type FetchFailure =
    { LastFailure: DateTimeOffset
      ConsecutiveFailures: int
      IsTimeout: bool }

let failureFilePath (cachePath: OsPath) = cachePath + ".failures"

let getBackoffHours failures =
    // Exponential backoff: 1hr, 2hrs, 4hrs, 8hrs, max 24hrs
    min 24.0 (Math.Pow(2.0, float (failures - 1)))

let getTimeoutBackoffMinutes failures =
    // 5min, 10min, 20min, ..., max 120min
    min 120.0 (5.0 * Math.Pow(2.0, float (failures - 1)))

let isCacheExpired (cacheConfig: CacheConfig) (modTime: DateTimeOffset) =
    (DateTimeOffset.Now - modTime) > cacheConfig.Expiration

let fileLastModified (path: OsPath) =
    if OsFile.exists path then
        OsFile.getLastWriteTime path |> DateTimeOffset |> Some
    else
        None

let readCache (cachePath: OsPath) =
    if OsFile.exists cachePath then
        Some(OsFile.readAllText cachePath)
    else
        None

let createDirectoryForPath (path: OsPath) =
    let (OsPath dir) = OsPath.getDirectoryName path

    if not (String.IsNullOrEmpty dir) then
        Directory.CreateDirectory dir |> ignore

let writeCache cachePath (content: string) =
    createDirectoryForPath cachePath
    OsFile.writeAllText cachePath content

let clearFailure cachePath =
    let failurePath = failureFilePath cachePath

    if OsFile.exists failurePath then
        OsFile.delete failurePath

let private freshFailure (isTimeout: bool) =
    { LastFailure = DateTimeOffset.Now
      ConsecutiveFailures = 1
      IsTimeout = isTimeout }

let private recordFailure (logger: ILogger) cachePath (isTimeout: bool) =
    let failurePath = failureFilePath cachePath
    createDirectoryForPath failurePath

    let failure =
        if OsFile.exists failurePath then
            try
                let json = OsFile.readAllText failurePath
                let existing = JsonSerializer.Deserialize<FetchFailure> json

                if existing.IsTimeout = isTimeout then
                    { existing with
                        LastFailure = DateTimeOffset.Now
                        ConsecutiveFailures = existing.ConsecutiveFailures + 1 }
                else
                    freshFailure isTimeout
            with ex ->
                logger.LogWarning(ex, "Failed to read existing failure record at {Path}, resetting count", failurePath)
                freshFailure isTimeout
        else
            freshFailure isTimeout

    OsFile.writeAllText failurePath (JsonSerializer.Serialize failure)

let recordHttpFailure (logger: ILogger) cachePath = recordFailure logger cachePath false
let recordTimeoutFailure (logger: ILogger) cachePath = recordFailure logger cachePath true

let readFailure (logger: ILogger) cachePath =
    let path = failureFilePath cachePath

    if OsFile.exists path then
        try
            let json = OsFile.readAllText path
            Some(JsonSerializer.Deserialize<FetchFailure> json)
        with ex ->
            logger.LogWarning(ex, "Failed to read failure record at {Path}", path)
            None
    else
        None

let nextRetry (logger: ILogger) cachePath =
    readFailure logger cachePath
    |> Option.map (fun failure ->
        if failure.IsTimeout then
            failure.LastFailure.AddMinutes(getTimeoutBackoffMinutes failure.ConsecutiveFailures)
        else
            failure.LastFailure.AddHours(getBackoffHours failure.ConsecutiveFailures))

let clearExpiredCache (logger: ILogger) (cacheDir: OsPath) (retention: TimeSpan) =
    if not (OsDirectory.exists cacheDir) then
        logger.LogWarning("Cache directory {Dir} does not exist", cacheDir)
    else
        let now = DateTime.Now

        OsDirectory.getFiles cacheDir
        |> Array.filter (fun f -> (now - OsFile.getLastWriteTime f) > retention)
        |> Array.iter OsFile.delete

let private invalidFilenameCharsRegex =
    RegularExpressions.Regex("[.?=:/]+", RegularExpressions.RegexOptions.Compiled)

let convertUrlToValidFilename (uri: Uri) =
    invalidFilenameCharsRegex.Replace(uri.AbsoluteUri, "_") |> Filename

let readFromCache (cacheConfig: CacheConfig) (memCache: InMemoryCache) (ups: UriProcessState) : UriProcessState =
    match ups with
    | TryFetchFromCache u ->
        match memCache.TryGet(u.AbsoluteUri, cacheConfig.Expiration) with
        | Some articles -> FeedArticles articles
        | None ->
            let cachePath = OsPath.combine cacheConfig.Dir (convertUrlToValidFilename u)
            let cacheModified = fileLastModified cachePath

            match cacheModified with
            | None -> PendingFetch(None, u)
            | Some modTime when isCacheExpired cacheConfig modTime -> PendingFetch(Some modTime, u)
            | Some _ ->
                match readCache cachePath with
                | Some s -> UnparsedCachedContent(s, u)
                | None -> PendingFetch(None, u)
    | ProcessingError e ->
        let (MessageUri uriStr) = e
        let feedUri = Uri uriStr
        let cachePath = OsPath.combine cacheConfig.Dir (convertUrlToValidFilename feedUri)

        match readCache cachePath with
        | Some content -> UnparsedStaleCachedContent(content, feedUri, e)
        | None -> ProcessingError e
    | _ -> ups

let cacheSuccessfulFetch cacheConfig ups =
    match ups with
    | ParsedLiveFeed(xml, feed) ->
        let cachePath =
            OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feed.Link))

        writeCache cachePath xml.Value
    | _ -> ()

    ups
