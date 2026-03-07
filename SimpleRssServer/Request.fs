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

        if not (isNull rssValues) && rssValues.Length > 0 then
            rssValues |> Array.map Uri.CreateWithHttps
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
            do! clearFailure cachePath
            return page
        | Error _ ->
            do! recordFailure cachePath
            return page
    }

type CacheState =
    | NoCacheNoFailures
    | ReadyToRetry
    | CacheExpired
    | InBackoffWithCache of waitTime: TimeSpan
    | InBackoffNoCache of waitTime: TimeSpan
    | CacheValid

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
    | Some _, None -> CacheValid

let fetchUrlWithCacheAsync client (cacheConfig: CacheConfig) (uri: Result<Uri, UriError>) =
    match uri with
    | Ok u ->
        let cacheFilename = convertUrlToValidFilename u
        let cachePath = Path.Combine(cacheConfig.Dir, cacheFilename)
        let cacheModified = fileLastModified cachePath

        let nextAttempt = nextRetry cachePath

        match computeCacheState cacheModified nextAttempt cacheConfig.Expiration with
        | NoCacheNoFailures
        | ReadyToRetry
        | CacheExpired -> fetchAndReadPage client u cacheModified cachePath
        | InBackoffWithCache waitTime ->
            async {
                let! cachedPage = readCache cachePath
                return Error(PreviousHttpRequestFailedButPageCached(u, waitTime, cachedPage |> Option.defaultValue ""))
            }
        | InBackoffNoCache waitTime -> async { return Error(PreviousHttpRequestFailed(u, waitTime)) }
        | CacheValid ->
            async {
                let! cache = readCache cachePath

                match cache with
                | Some page -> return Ok page
                | None -> return Error(CacheReadFailed(u, cachePath))
            }
    | Error e ->
        match e with
        | UriError.HostNameMustContainDot e -> async { return Error(InvalidUriHostname e) }
        | UriError.UriFormatException(e, ex) -> async { return Error(InvalidUriFormat(e, ex)) }

let cacheSuccessfulFetch (cacheConfig: CacheConfig) (uri: Uri) (content: string) =
    async {
        let cachePath = Path.Combine(cacheConfig.Dir, convertUrlToValidFilename uri)
        do! writeCache cachePath content
    }

let fetchAllRssFeeds client (cacheConfig: CacheConfig) (uris: Result<Uri, UriError> array) =
    uris
    |> Array.map (fetchUrlWithCacheAsync client cacheConfig)
    |> Async.Parallel
    |> Async.RunSynchronously
