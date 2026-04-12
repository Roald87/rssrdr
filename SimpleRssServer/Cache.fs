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
      ConsecutiveFailures: int }

let failureFilePath (cachePath: OsPath) = cachePath + ".failures"

let getBackoffHours failures =
    // Exponential backoff: 1hr, 2hrs, 4hrs, 8hrs, max 24hrs
    min 24.0 (Math.Pow(2.0, float (failures - 1)))

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

let recordFailure (logger: ILogger) cachePath =
    let failurePath = failureFilePath cachePath
    createDirectoryForPath failurePath

    let failure =
        if OsFile.exists failurePath then
            try
                let json = OsFile.readAllText failurePath
                let existing = JsonSerializer.Deserialize<FetchFailure> json

                { LastFailure = DateTimeOffset.Now
                  ConsecutiveFailures = existing.ConsecutiveFailures + 1 }
            with ex ->
                logger.LogWarning(ex, "Failed to read existing failure record at {Path}, resetting count", failurePath)

                { LastFailure = DateTimeOffset.Now
                  ConsecutiveFailures = 1 }
        else
            { LastFailure = DateTimeOffset.Now
              ConsecutiveFailures = 1 }

    OsFile.writeAllText failurePath (JsonSerializer.Serialize failure)

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
    match readFailure logger cachePath with
    | None -> None // No failures recorded or can't read failure file
    | Some failure ->
        let backoffHours = getBackoffHours failure.ConsecutiveFailures
        Some(failure.LastFailure.AddHours backoffHours)

let clearExpiredCache (logger: ILogger) (cacheDir: OsPath) (retention: TimeSpan) =
    if not (OsDirectory.exists cacheDir) then
        logger.LogWarning("Cache directory {Dir} does not exist", cacheDir)
    else
        let now = DateTime.Now

        OsDirectory.getFiles cacheDir
        |> Array.filter (fun f -> (now - File.GetLastWriteTime f) > retention)
        |> Array.iter File.Delete

let convertUrlToValidFilename (uri: Uri) =
    let replaceInvalidFilenameChars = RegularExpressions.Regex "[.?=:/]+"
    replaceInvalidFilenameChars.Replace(uri.AbsoluteUri, "_") |> Filename

let readFromCache (cacheConfig: CacheConfig) (memCache: InMemoryCache) (ups: UriProcessState) : UriProcessState =
    match ups with
    | ValidUri(_, u) ->
        match memCache.TryGet(u.AbsoluteUri, cacheConfig.Expiration) with
        | Some articles -> FeedArticles articles
        | None ->
            let cachePath = OsPath.combine cacheConfig.Dir (convertUrlToValidFilename u)
            let cacheModified = fileLastModified cachePath

            match cacheModified with
            | None -> ValidUri(None, u)
            | Some modTime when (DateTimeOffset.Now - modTime) <= cacheConfig.Expiration ->
                match readCache cachePath with
                | Some s -> CachedFeed(s, u)
                | None -> ValidUri(None, u)
            | Some modTime -> ValidUri(Some modTime, u)
    | ProcessingError e ->
        let (MessageUri uriStr) = e
        let feedUri = Uri uriStr
        let cachePath = OsPath.combine cacheConfig.Dir (convertUrlToValidFilename feedUri)

        match readCache cachePath with
        | Some content -> StaleHitWithError(content, feedUri, e)
        | None -> ProcessingError e
    | _ -> ups

let cacheSuccessfulFetch cacheConfig ups =
    match ups with
    | ParsedFeed(xml, feed) ->
        let cachePath =
            OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feed.Link))

        writeCache cachePath xml.Value
    | _ -> ()

    ups
