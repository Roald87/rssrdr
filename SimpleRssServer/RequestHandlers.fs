module SimpleRssServer.RequestHandlers

open Microsoft.Extensions.Logging
open System.IO
open System.Net
open System.Text

open SimpleRssServer.AppContext
open SimpleRssServer.Cache
open SimpleRssServer.Collections
open SimpleRssServer.Config
open SimpleRssServer.Helper
open SimpleRssServer.HtmlRenderer
open SimpleRssServer.MemoryCache
open SimpleRssServer.Request
open SimpleRssServer.RequestLog
open SimpleRssServer.Router
open SimpleRssServer.RssParser
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes

let private readFormBody (httpCtx: HttpListenerContext) =
    async {
        use reader = new StreamReader(httpCtx.Request.InputStream, Encoding.UTF8)
        let! body = reader.ReadToEndAsync() |> Async.AwaitTask
        return Query.Create("?" + body)
    }

let processRssRequest (appCtx: AppContext) (query: string) =
    let readCache = readFromCache appCtx.CacheConfig appCtx.MemCache
    let applyBackoff = applyBackoff appCtx.Logger appCtx.CacheConfig

    let fetchRssFeeds =
        fetchAllRssFeeds appCtx.Client appCtx.Logger appCtx.CacheConfig UserFetchConfig

    getRssUrls query
    |> List.map (toUriProcessState >> readCache >> applyBackoff) // read cache, then skip feeds still in backoff
    |> fetchRssFeeds
    |> Async.RunSynchronously
    |> List.map (readCache >> parseFeedResult appCtx.Logger) // read from cache in case of 304 Not modified
    |> List.collect checkIfDiscoveryFeeds
    |> List.map (readCache >> applyBackoff) // read discovered feeds from cache, then apply backoff
    |> fetchRssFeeds
    |> Async.RunSynchronously
    |> List.map (
        readCache // previous fetch can contain 304s
        >> parseFeedResult appCtx.Logger
        >> cacheSuccessfulFetch appCtx.CacheConfig
    )
    |> logSuccessfulFeedRequestsAndParses appCtx.RequestLogPath
    |> List.map (feedToArticles >> updateMemoryCache appCtx.MemCache)
    |> List.collect onlyFeedArticles

let buildProcessedQuery (articles: Article list) : Query =
    articles
    |> List.map (fun a -> FeedUri.removeHttpsScheme a.FeedUrl)
    |> List.distinct
    |> fun u -> Query.CreateWithKey("rss", u)

let private robotsTxt = File.ReadAllText(Path.Combine("site", "robots.txt"))
let private sitemapXml = File.ReadAllText(Path.Combine("site", "sitemap.xml"))

let private appleTouchIcon =
    File.ReadAllBytes(Path.Combine("site", "apple-touch-icon.png"))

let private getSortedRssUris (q: Query) = q.GetValues "rss" |> List.sort

let private writeResponse (httpCtx: HttpListenerContext) (content: string) =
    async {
        let buffer = content |> Encoding.UTF8.GetBytes
        httpCtx.Response.ContentLength64 <- int64 buffer.Length
        httpCtx.Response.ContentType <- "text/html"

        do!
            httpCtx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length)
            |> Async.AwaitTask

        httpCtx.Response.OutputStream.Close()
    }

let private writeBytesResponse (httpCtx: HttpListenerContext) (contentType: string) (bytes: byte[]) =
    async {
        httpCtx.Response.ContentLength64 <- int64 bytes.Length
        httpCtx.Response.ContentType <- contentType

        do!
            httpCtx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length)
            |> Async.AwaitTask

        httpCtx.Response.OutputStream.Close()
    }

let private writeChunk (httpCtx: HttpListenerContext) (html: Html) =
    async {
        let bytes = html |> string |> Encoding.UTF8.GetBytes

        do!
            httpCtx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length)
            |> Async.AwaitTask

        do! httpCtx.Response.OutputStream.FlushAsync() |> Async.AwaitTask
    }

let private redirectTo (httpCtx: HttpListenerContext) (url: string) =
    async {
        httpCtx.Response.Redirect url
        httpCtx.Response.OutputStream.Close()
    }

let private streamChunkedPage (httpCtx: HttpListenerContext) (shell: Html) (renderContent: unit -> Async<Html>) =
    async {
        httpCtx.Response.SendChunked <- true
        httpCtx.Response.ContentType <- "text/html"
        do! writeChunk httpCtx shell

        let! content = renderContent ()
        do! writeChunk httpCtx content
        httpCtx.Response.OutputStream.Close()
    }

/// Streams a feeds page for the current request's own `?rss=` query, redirecting first
/// if fetching/discovery normalized the feed list (e.g. stripped a scheme, followed a discovery link).
let private streamFeedResponse
    (appCtx: AppContext)
    (httpCtx: HttpListenerContext)
    (shell: Html)
    (render: Query -> Article list -> Html)
    =
    streamChunkedPage httpCtx shell (fun () ->
        async {
            let articles = processRssRequest appCtx httpCtx.Request.Url.Query

            let originalQuery = Query.Create httpCtx.Request.Url.Query
            let processedQuery = buildProcessedQuery articles

            return
                if getSortedRssUris originalQuery <> getSortedRssUris processedQuery then
                    metaRefreshContent (string processedQuery)
                else
                    render processedQuery articles
        })

let private handleConfigPage (appCtx: AppContext) (httpCtx: HttpListenerContext) =
    async {
        let query = Query.Create httpCtx.Request.Url.Query

        match query.GetValues "s" |> List.tryHead with
        | Some rawId ->
            let collectionId = CollectionId rawId

            match tryLoad appCtx.CollectionsDir collectionId with
            | None -> do! writeResponse httpCtx (collectionNotFoundPage collectionId |> string)
            | Some feeds ->
                let rssUrls = feeds |> List.map FeedUri.createWithHttps
                do! writeResponse httpCtx (configPage rssUrls (Some collectionId) |> string)
        | None -> do! writeResponse httpCtx (configPage (getRssUrls httpCtx.Request.Url.Query) None |> string)
    }

let private handleCreateCollection (appCtx: AppContext) (httpCtx: HttpListenerContext) =
    async {
        let! formData = readFormBody httpCtx
        let feeds = formData.GetValues "rss"
        let collectionId = generateCollectionId ()
        save appCtx.CollectionsDir collectionId feeds
        do! redirectTo httpCtx $"/s/{collectionId}"
    }

let private handleUpdateCollection (appCtx: AppContext) (httpCtx: HttpListenerContext) (collectionId: CollectionId) =
    async {
        if isValidCollectionId collectionId then
            let! formData = readFormBody httpCtx
            let feeds = formData.GetValues "rss"
            save appCtx.CollectionsDir collectionId feeds
            do! redirectTo httpCtx $"/s/{collectionId}"
        else
            do! writeResponse httpCtx (collectionNotFoundPage collectionId |> string)
    }

let private handleViewCollection
    (appCtx: AppContext)
    (httpCtx: HttpListenerContext)
    (collectionId: CollectionId)
    (shell: Html)
    (content: Query -> Article list -> Html)
    =
    async {
        match tryLoad appCtx.CollectionsDir collectionId with
        | None -> do! writeResponse httpCtx (collectionNotFoundPage collectionId |> string)
        | Some feeds ->
            touch appCtx.CollectionsDir collectionId
            let collQuery = Query.CreateWithKey("rss", feeds)

            do!
                streamChunkedPage httpCtx shell (fun () ->
                    async { return content collQuery (processRssRequest appCtx (string collQuery)) })
    }

let handleRequest (appCtx: AppContext) (httpCtx: HttpListenerContext) =
    async {
        appCtx.Logger.LogDebug $"Received request {httpCtx.Request.Url}"

        match parseRoute httpCtx.Request.HttpMethod httpCtx.Request.RawUrl with
        | ConfigPage -> do! handleConfigPage appCtx httpCtx
        | ShuffleFeeds ->
            let query = Query.Create httpCtx.Request.Url.Query

            do! streamFeedResponse appCtx httpCtx (shuffledFeedsPageShell query) shuffledFeedsPageContent
        | ChronologicalFeeds ->
            let query = Query.Create httpCtx.Request.Url.Query

            do! streamFeedResponse appCtx httpCtx (chronologicalFeedsPageShell query) chronologicalFeedsPageContent
        | RobotsTxt -> do! writeResponse httpCtx robotsTxt
        | SitemapXml -> do! writeResponse httpCtx sitemapXml
        | AppleTouchIcon -> do! writeBytesResponse httpCtx "image/png" appleTouchIcon
        | CreateCollection -> do! handleCreateCollection appCtx httpCtx
        | ViewCollectionShuffle collectionId ->
            do!
                handleViewCollection
                    appCtx
                    httpCtx
                    collectionId
                    (collectionShuffledPageShell collectionId)
                    shuffledFeedsPageContent
        | ViewCollection collectionId ->
            do!
                handleViewCollection
                    appCtx
                    httpCtx
                    collectionId
                    (collectionFeedsPageShell collectionId)
                    chronologicalFeedsPageContent
        | UpdateCollection collectionId -> do! handleUpdateCollection appCtx httpCtx collectionId
        | LandingPage -> do! writeResponse httpCtx (landingPage |> string)
    }

let handleRequestSafely (appCtx: AppContext) httpCtx =
    async {
        try
            do! handleRequest appCtx httpCtx
        with ex ->
            appCtx.Logger.LogInformation("Request handling error: {Message}", ex.Message)
    }
