module SimpleRssServer.Request

open System
open System.IO
open System.Text

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

type CacheState =
    | NoCacheNoFailures
    | ReadyToRetry
    | CacheExpired
    | InBackoffWithCache of waitTime: TimeSpan
    | InBackoffNoCache of waitTime: TimeSpan
    | CacheHit

let computeCacheAndBackoffState
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

let private fetchUri client logger (cacheConfig: CacheConfig) (dt, uri) =
    async {
        let cachePath = Path.Combine(cacheConfig.Dir, convertUrlToValidFilename uri)

        let cacheState =
            computeCacheAndBackoffState dt (nextRetry cachePath) cacheConfig.Expiration

        match cacheState with
        | InBackoffWithCache waitTime -> return ProcessingError(PreviousHttpRequestFailedButPageCached(uri, waitTime))
        | InBackoffNoCache waitTime -> return ProcessingError(PreviousHttpRequestFailed(uri, waitTime))
        | _ ->
            let! r = fetchUrlAsync client logger uri dt RequestTimeout

            return
                match r with
                | Ok "No changes" ->
                    File.SetLastWriteTime(cachePath, DateTime.Now)
                    clearFailure cachePath
                    // TODO there should not be any cache reading in this method, move up
                    match readCache cachePath with
                    | Some content -> CachedFeed(content, uri)
                    | None -> ProcessingError(CacheReadFailed(uri, cachePath))
                | Ok content ->
                    clearFailure cachePath
                    Response(content, uri)
                | Error e ->
                    recordFailure cachePath
                    ProcessingError e
    }

let fetchAllRssFeeds client logger (cacheConfig: CacheConfig) (ups: UriProcessState array) =
    let validUris =
        ups
        |> Array.choose (function
            | ValidUri(dt, uri) -> Some(dt, uri)
            | _ -> None)

    let invalidUrls =
        ups
        |> Array.filter (function
            | ValidUri _ -> false
            | _ -> true)

    async {
        let! processed = validUris |> Array.map (fetchUri client logger cacheConfig) |> Async.Parallel
        return Array.append processed invalidUrls
    }
