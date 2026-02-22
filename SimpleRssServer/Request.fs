module SimpleRssServer.Request

open Microsoft.Extensions.Logging
open System
open System.IO
open System.Text
open System.Web

open SimpleRssServer.Logging
open SimpleRssServer.HttpClient
open SimpleRssServer.Cache
open SimpleRssServer.Config
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.DomainModel

let convertUrlToValidFilename (uri: Uri) =
    let replaceInvalidFilenameChars = RegularExpressions.Regex "[.?=:/]+"
    replaceInvalidFilenameChars.Replace(uri.AbsoluteUri, "_") |> Filename

let getRssUrls (context: string) : Result<Uri, UriError> array =
    context
    |> HttpUtility.ParseQueryString
    |> fun query ->
        let rssValues = query.GetValues "rss"

        if rssValues <> null && rssValues.Length > 0 then
            rssValues |> Array.map (fun s -> Uri.createWithHttps s)
        else
            [||]

let fetchAndReadPage client (uri: Uri) cacheModified cachePath =
    async {
        logger.LogDebug $"Fetching {uri}"
        let! page = fetchUrlAsync client logger uri cacheModified RequestTimeout

        match page with
        | Ok "No changes" ->
            try
                logger.LogDebug $"Reading from cached file {cachePath}, because feed didn't change"
                let! content = readCache cachePath
                File.SetLastWriteTime(cachePath, DateTime.Now)
                do! clearFailure cachePath
                return Ok content.Value
            with ex ->
                logger.LogError $"Failed to read file {cachePath}. {ex.GetType().Name}: {ex.Message}"
                do! recordFailure cachePath
                return Error(CacheReadFailedWithException(uri, cachePath, ex))
        | Ok content ->
            do! writeCache cachePath content
            do! clearFailure cachePath
            return page
        | Error _ ->
            do! recordFailure cachePath
            return page
    }

let fetchUrlWithCacheAsync client (cacheConfig: CacheConfig) (uri: Uri) =
    let cacheFilename = convertUrlToValidFilename uri
    let cachePath = Path.Combine(cacheConfig.Dir, cacheFilename)
    let cacheModified = fileLastModified cachePath

    let nextAttempt = nextRetry cachePath

    match cacheModified, nextAttempt with
    | None, None -> fetchAndReadPage client uri cacheModified cachePath
    | _, Some d when (d < DateTimeOffset.Now) -> fetchAndReadPage client uri cacheModified cachePath
    | Some d, None when (DateTimeOffset.Now - d) > cacheConfig.Expiration ->
        fetchAndReadPage client uri cacheModified cachePath
    | _, Some d ->
        let waitTime = TimeSpan.FromHours (d - DateTimeOffset.Now).TotalHours
        async { return Error(HttpRequestFailed(uri, waitTime)) }
    | Some d, None ->
        async {
            let! cache = readCache cachePath

            match cache with
            | Some page -> return Ok page
            | None -> return Error(CacheReadFailed(uri, cachePath))
        }

let fetchAllRssFeeds client (cacheConfig: CacheConfig) (uris: Uri array) =
    uris
    |> Array.map (fetchUrlWithCacheAsync client cacheConfig)
    |> Async.Parallel
    |> Async.RunSynchronously

let notEmpty (s: string) = not (String.IsNullOrWhiteSpace s)
