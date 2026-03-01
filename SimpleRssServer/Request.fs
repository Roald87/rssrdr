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

let fetchUrlWithCacheAsync client (cacheConfig: CacheConfig) (uri: Result<Uri, UriError>) =
    match uri with
    | Ok u ->
        let cacheFilename = convertUrlToValidFilename u
        let cachePath = Path.Combine(cacheConfig.Dir, cacheFilename)
        let cacheModified = fileLastModified cachePath

        let nextAttempt = nextRetry cachePath

        match cacheModified, nextAttempt with
        | None, None -> fetchAndReadPage client u cacheModified cachePath
        | _, Some d when (d < DateTimeOffset.Now) -> fetchAndReadPage client u cacheModified cachePath
        | Some d, None when (DateTimeOffset.Now - d) > cacheConfig.Expiration ->
            fetchAndReadPage client u cacheModified cachePath
        | Some _, Some _ ->
            let waitTime =
                TimeSpan.FromHours (nextAttempt.Value - DateTimeOffset.Now).TotalHours

            let cachedPage: string =
                readCache cachePath |> Async.RunSynchronously |> Option.defaultValue ""

            async { return Error(PreviousHttpRequestFailedButPageCached(u, waitTime, cachedPage)) }
        | _, Some d ->
            let waitTime = TimeSpan.FromHours (d - DateTimeOffset.Now).TotalHours
            async { return Error(PreviousHttpRequestFailed(u, waitTime)) }
        | Some _, None ->
            async {
                let! cache = readCache cachePath

                match cache with
                | Some page -> return Ok page
                | None -> return Error(CacheReadFailed(u, cachePath))
            }
    | Error e ->
        match e with
        | UriError.HostNameMustContainDot e -> async { return Error(UriHostNameMustContainDot e) }
        | UriError.UriFormatException(e, ex) -> async { return Error(UriFormatException(e, ex)) }

let fetchAllRssFeeds client (cacheConfig: CacheConfig) (uris: Result<Uri, UriError> array) =
    uris
    |> Array.map (fetchUrlWithCacheAsync client cacheConfig)
    |> Async.Parallel
    |> Async.RunSynchronously
