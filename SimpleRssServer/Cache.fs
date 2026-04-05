module SimpleRssServer.Cache

open Microsoft.Extensions.Logging
open System
open System.IO
open System.Text
open System.Text.Json

open SimpleRssServer.Config
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.Logging
open SimpleRssServer.MemoryCache

type FetchFailure =
    { LastFailure: DateTimeOffset
      ConsecutiveFailures: int }

let failureFilePath (cachePath: OsPath) = cachePath + ".failures"

let getBackoffHours failures =
    // Exponential backoff: 1hr, 2hrs, 4hrs, 8hrs, max 24hrs
    min 24.0 (Math.Pow(2.0, float (failures - 1)))

let fileLastModified (path: OsPath) =
    if File.Exists path then
        File.GetLastWriteTime path |> DateTimeOffset |> Some
    else
        None

let readCache (cachePath: OsPath) =
    if File.Exists cachePath then
        Some(File.ReadAllText cachePath)
    else
        None

let createDirectoryForPath (path: OsPath) =
    let (OsPath dir) = Path.GetDirectoryName path

    dir |> Option.ofObj |> Option.iter (Directory.CreateDirectory >> ignore)

let writeCache cachePath (content: string) =
    createDirectoryForPath cachePath
    File.WriteAllText(cachePath, content)

let clearFailure cachePath =
    let failurePath = failureFilePath cachePath

    if File.Exists failurePath then
        File.Delete failurePath

let recordFailure cachePath =
    let failurePath = failureFilePath cachePath
    createDirectoryForPath failurePath

    let failure =
        if File.Exists(failurePath) then
            try
                let json = File.ReadAllText(failurePath)
                let existing = JsonSerializer.Deserialize<FetchFailure>(json)

                { LastFailure = DateTimeOffset.Now
                  ConsecutiveFailures = existing.ConsecutiveFailures + 1 }
            with _ ->
                { LastFailure = DateTimeOffset.Now
                  ConsecutiveFailures = 1 }
        else
            { LastFailure = DateTimeOffset.Now
              ConsecutiveFailures = 1 }

    File.WriteAllText(failurePath, JsonSerializer.Serialize(failure))

let readFailure cachePath =
    let path = failureFilePath cachePath

    if File.Exists(path) then
        try
            let json = File.ReadAllText(path)
            Some(JsonSerializer.Deserialize<FetchFailure>(json))
        with _ ->
            None
    else
        None

let nextRetry cachePath =
    match readFailure cachePath with
    | None -> None // No failures recorded or can't read failure file
    | Some failure ->
        let backoffHours = getBackoffHours failure.ConsecutiveFailures
        Some(failure.LastFailure.AddHours backoffHours)

let clearExpiredCache (cacheDir: OsPath) (retention: TimeSpan) =
    if not (Directory.Exists cacheDir) then
        logger.LogWarning("Cache directory {Dir} does not exist", cacheDir)
    else
        let now = DateTime.Now

        Directory.GetFiles cacheDir
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

            let cachePath = Path.Combine(cacheConfig.Dir, convertUrlToValidFilename u)
            let cacheModified = fileLastModified cachePath

            match cacheModified with
            | None -> ValidUri(None, u)
            | Some modTime when (DateTimeOffset.Now - modTime) <= cacheConfig.Expiration ->
                match readCache cachePath with
                | Some s -> CachedFeed(s, u)
                | None -> ValidUri(None, u)
            | Some modTime -> ValidUri(Some modTime, u)
    | ProcessingError e ->
        match e.Uri with
        | Some uriStr ->
            let feedUri = Uri uriStr
            let cachePath = Path.Combine(cacheConfig.Dir, convertUrlToValidFilename feedUri)

            match readCache cachePath with
            | Some content -> StaleHitWithError(content, feedUri, e)
            | None -> ProcessingError e
        | None -> ProcessingError e
    | _ -> ups

let cacheSuccessfulFetch cacheConfig ups =
    match ups with
    | ParsedFeed(xml, feed) ->
        let cachePath =
            Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feed.Link))

        writeCache cachePath xml.Value
    | _ -> ()

    ups
