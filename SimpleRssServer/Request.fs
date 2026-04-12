module SimpleRssServer.Request

open System
open System.IO

open SimpleRssServer.Cache
open SimpleRssServer.Config
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.HttpClient

let getRssUrls (query: string) : Result<Uri, UriError> list =
    Query.Create query
    |> fun q -> q.GetValues "rss"
    |> List.map FeedUri.createWithHttps

type CacheState =
    | NoCacheNoFailures
    | ReadyToRetry
    | CacheExpired
    | InBackoffWithCache of waitTime: TimeSpan
    | InBackoffNoCache of waitTime: TimeSpan
    | CacheHit

let computeCacheAndBackoffState cacheModified nextAttempt expiration =
    match cacheModified, nextAttempt with
    | None, None -> NoCacheNoFailures
    | _, Some na when na < DateTimeOffset.Now -> ReadyToRetry
    | Some cm, None when (DateTimeOffset.Now - cm) > expiration -> CacheExpired
    | Some _, Some na -> InBackoffWithCache(TimeSpan.FromHours (na - DateTimeOffset.Now).TotalHours)
    | _, Some na -> InBackoffNoCache(TimeSpan.FromHours (na - DateTimeOffset.Now).TotalHours)
    | Some _, None -> CacheHit

let private fetchUri client logger (cacheConfig: CacheConfig) (dt, uri) =
    async {
        let cachePath = OsPath.combine cacheConfig.Dir (convertUrlToValidFilename uri)

        let cacheState =
            computeCacheAndBackoffState dt (nextRetry logger cachePath) cacheConfig.Expiration

        match cacheState with
        | InBackoffWithCache waitTime -> return ProcessingError(PreviousHttpRequestFailedButPageCached(uri, waitTime))
        | InBackoffNoCache waitTime -> return ProcessingError(PreviousHttpRequestFailed(uri, waitTime))
        | _ ->
            let! r = fetchUrlAsync client logger uri dt RequestTimeout

            return
                match r with
                | Ok "No changes" ->
                    OsFile.setLastWriteTime cachePath DateTime.Now
                    clearFailure cachePath
                    ValidUri(Some DateTimeOffset.Now, uri)
                | Ok content ->
                    clearFailure cachePath
                    Response(content, uri)
                | Error e ->
                    recordFailure logger cachePath
                    ProcessingError e
    }

let fetchAllRssFeeds client logger (cacheConfig: CacheConfig) (ups: UriProcessState list) =
    let validUris, invalidUrls =
        List.fold
            (fun (valid, invalid) ups ->
                match ups with
                | ValidUri(dt, uri) -> (dt, uri) :: valid, invalid
                | x -> valid, x :: invalid)
            ([], [])
            ups

    async {
        let! processed = validUris |> List.map (fetchUri client logger cacheConfig) |> Async.Parallel
        return List.ofArray processed @ invalidUrls
    }
