module SimpleRssServer.Request

open System

open SimpleRssServer.Cache
open SimpleRssServer.Config
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.HttpClient

let getRssUrls (query: string) : Result<Uri, UriError> list =
    (Query.Create query).GetValues "rss" |> List.map FeedUri.createWithHttps

let private fetchUri client logger (cacheConfig: CacheConfig) (fetchConfig: FetchConfig) (dt, uri) =
    async {
        let cachePath = OsPath.combine cacheConfig.Dir (convertUrlToValidFilename uri)

        let! r = fetchUrlAsync client logger uri dt fetchConfig.Timeout

        return
            match r with
            | Ok NotModified ->
                OsFile.setLastWriteTime cachePath DateTime.Now
                clearFailure cachePath
                TryFetchFromCache uri
            | Ok(Content content) ->
                clearFailure cachePath
                UnparsedHttpResponse(content, uri)
            | Error e ->
                recordFailure logger cachePath e
                ProcessingError e
    }

let fetchRssFeeds client logger (cacheConfig: CacheConfig) (fetchConfig: FetchConfig) (upss: UriProcessState list) =
    async {
        let! processed =
            upss
            |> List.map (function
                | PendingFetch(dt, uri) -> fetchUri client logger cacheConfig fetchConfig (dt, uri)
                | x -> async.Return x)
            |> fun asyncs -> Async.Parallel(asyncs, fetchConfig.MaxParallelism)

        return List.ofArray processed
    }
