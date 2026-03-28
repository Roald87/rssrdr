module SimpleRssServer.Request

open Microsoft.Extensions.Logging
open System
open System.IO
open System.Text

open SimpleRssServer.Logging
open SimpleRssServer.HttpClient
open SimpleRssServer.Cache
open SimpleRssServer.Config
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.DomainModel

let convertUrlToValidFilename (uri: Uri) =
    let replaceInvalidFilenameChars = RegularExpressions.Regex "[.?=:/]+"
    replaceInvalidFilenameChars.Replace(uri.AbsoluteUri, "_") |> Filename

let getRssUrls (query: string) : Result<Uri, UriError> array =
    Query.Create query
    |> fun query -> query.GetValues "rss"
    |> fun (rssUrls: string array) ->

        if not (isNull rssUrls) && rssUrls.Length > 0 then
            rssUrls |> Array.map Uri.CreateWithHttps
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
                let content = readCache cachePath
                File.SetLastWriteTime(cachePath, DateTime.Now)
                clearFailure cachePath
                return Ok content.Value
            with ex ->
                logger.LogError $"Failed to read file {cachePath}. {ex.GetType().Name}: {ex.Message}"
                recordFailure cachePath
                return Error(CacheReadFailedWithException(uri, cachePath, ex))
        | Ok content ->
            clearFailure cachePath
            return page
        | Error _ ->
            recordFailure cachePath
            return page
    }

type CacheState =
    | NoCacheNoFailures
    | ReadyToRetry
    | CacheExpired
    | InBackoffWithCache of waitTime: TimeSpan
    | InBackoffNoCache of waitTime: TimeSpan
    | CacheHit

let computeCacheState
    (cacheModified: DateTimeOffset option)
    (nextAttempt: DateTimeOffset option)
    (expiration: TimeSpan)
    =
    match cacheModified, nextAttempt with
    | None, None -> NoCacheNoFailures
    | _, Some na when na < DateTimeOffset.Now -> ReadyToRetry
    | Some cm, None when (DateTimeOffset.Now - cm) > expiration -> CacheExpired
    | Some _, Some na -> InBackoffWithCache(TimeSpan.FromHours (na - DateTimeOffset.Now).TotalHours)
    | _, Some na -> InBackoffNoCache(TimeSpan.FromHours (na - DateTimeOffset.Now).TotalHours)
    | Some _, None -> CacheHit

let fetchUrlWithCacheAsync client (cacheConfig: CacheConfig) (uri: Result<Uri, UriError>) =
    async {
        match uri with
        | Error(UriError.HostNameMustContainDot e) -> return Failed(InvalidUriHostname e)
        | Error(UriError.UriFormatException(e, ex)) -> return Failed(InvalidUriFormat(e, ex))
        | Ok u ->
            let cacheFilename = convertUrlToValidFilename u
            let cachePath = Path.Combine(cacheConfig.Dir, cacheFilename)
            let cacheModified = fileLastModified cachePath
            let nextAttempt = nextRetry cachePath

            match computeCacheState cacheModified nextAttempt cacheConfig.Expiration with
            | NoCacheNoFailures
            | ReadyToRetry
            | CacheExpired ->
                let! result = fetchAndReadPage client u cacheModified cachePath

                return
                    match result with
                    | Ok content -> FreshContent(content, u)
                    | Error e -> Failed e
            | InBackoffWithCache waitTime ->
                return
                    match readCache cachePath with
                    | Some content -> CachedContent(content, PreviousHttpRequestFailedButPageCached(u, waitTime))
                    | None -> Failed(CacheReadFailed(u, cachePath))
            | InBackoffNoCache waitTime -> return Failed(PreviousHttpRequestFailed(u, waitTime))
            | CacheHit ->
                return
                    match readCache cachePath with
                    | Some page -> FreshContent(page, u)
                    | None -> Failed(CacheReadFailed(u, cachePath))
    }

let cacheSuccessfulFetch (cacheConfig: CacheConfig) (feedUri: FeedUri) (content: string) =
    let cachePath = Path.Combine(cacheConfig.Dir, convertUrlToValidFilename feedUri.Uri)
    writeCache cachePath content

let fetchAllRssFeeds client (cacheConfig: CacheConfig) (uris: Result<Uri, UriError> array) =
    uris |> Array.map (fetchUrlWithCacheAsync client cacheConfig) |> Async.Parallel

let fetchAllRssFeeds2 client logger (uris: UriProcessState array) =
    let validUris =
        uris
        |> Array.choose (function
            | ValidUri(dt, uri) -> Some(dt, uri)
            | _ -> None)

    let rest =
        uris
        |> Array.filter (function
            | ValidUri _ -> false
            | _ -> true)

    async {
        let! results =
            validUris
            |> Array.map (fun (dt, uri) ->
                async {
                    let! r = fetchUrlAsync client logger uri dt RequestTimeout
                    return uri, r
                })
            |> Async.Parallel

        let processed =
            results
            |> Array.map (function
                | (uri, Ok content) -> Response(content, uri)
                | (_, Error e) -> ProcessingError e)

        return Array.append processed rest
    }
