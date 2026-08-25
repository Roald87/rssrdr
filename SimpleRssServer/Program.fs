open Microsoft.Extensions.Logging
open System
open System.IO
open System.Net
open System.Text

open SimpleRssServer.Cache
open SimpleRssServer.Collections
open SimpleRssServer.Config
open SimpleRssServer.Helper
open SimpleRssServer.HtmlRenderer
open SimpleRssServer.Logging
open SimpleRssServer.MemoryCache
open SimpleRssServer.Request
open SimpleRssServer.RequestLog
open SimpleRssServer.RssParser
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes

type AppContext =
    { Client: Http.HttpClient
      Logger: ILogger
      CacheConfig: CacheConfig
      MemCache: InMemoryCache }

let private readFormBody (context: HttpListenerContext) =
    async {
        use reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)
        let! body = reader.ReadToEndAsync() |> Async.AwaitTask
        return Query.Create("?" + body)
    }

let processRssRequest (ctx: AppContext) (logPath: OsPath) (query: string) =
    let readCache = readFromCache ctx.CacheConfig ctx.MemCache

    getRssUrls query
    |> List.map (toUriProcessState >> readCache) // try read cache before first fetch
    |> fetchAllRssFeeds ctx.Client ctx.Logger ctx.CacheConfig UserFetchConfig
    |> Async.RunSynchronously
    |> List.map (readCache >> parseFeedResult ctx.Logger) // read from cache in case of 304 Not modified
    |> List.collect checkIfDiscoveryFeeds
    |> List.map readCache // read discovered feeds from cache
    |> fetchAllRssFeeds ctx.Client ctx.Logger ctx.CacheConfig UserFetchConfig
    |> Async.RunSynchronously
    |> List.map (
        readCache // previous fetch can contain 304s
        >> parseFeedResult ctx.Logger
        >> cacheSuccessfulFetch ctx.CacheConfig
    )
    |> logSuccessfulFeedRequestsAndParses logPath
    |> List.map (feedToArticles >> updateMemoryCache ctx.MemCache)
    |> List.collect onlyFeedArticles

let getFeedUrlQuery articles =
    articles
    |> List.map _.FeedUrl
    |> List.distinct
    |> fun u -> Query.CreateWithKey("rss", u)

let buildProcessedQuery (articles: Article list) : Query =
    articles
    |> List.map (fun a -> FeedUri.removeHttpsScheme a.FeedUrl)
    |> List.distinct
    |> fun u -> Query.CreateWithKey("rss", u)

let private getSortedRssUris (q: Query) = q.GetValues "rss" |> List.sort

let private writeResponse (context: HttpListenerContext) (content: string) =
    async {
        let buffer = content |> Encoding.UTF8.GetBytes
        context.Response.ContentLength64 <- int64 buffer.Length
        context.Response.ContentType <- "text/html"

        do!
            context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length)
            |> Async.AwaitTask

        context.Response.OutputStream.Close()
    }

let private writeChunk (context: HttpListenerContext) (html: Html) =
    async {
        let bytes = html |> string |> Encoding.UTF8.GetBytes

        do!
            context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length)
            |> Async.AwaitTask

        do! context.Response.OutputStream.FlushAsync() |> Async.AwaitTask
    }

let private redirectTo (context: HttpListenerContext) (url: string) =
    async {
        context.Response.Redirect url
        context.Response.OutputStream.Close()
    }

/// Streams a feeds page for the current request's own `?rss=` query, redirecting first
/// if fetching/discovery normalized the feed list (e.g. stripped a scheme, followed a discovery link).
let private streamFeedResponse
    (ctx: AppContext)
    (context: HttpListenerContext)
    (shell: Html)
    (render: Query -> Article list -> Html)
    =
    async {
        context.Response.SendChunked <- true
        context.Response.ContentType <- "text/html"
        do! writeChunk context shell

        let articles = processRssRequest ctx RequestLogPath context.Request.Url.Query

        let originalQuery = Query.Create context.Request.Url.Query
        let processedQuery = buildProcessedQuery articles

        let content =
            if getSortedRssUris originalQuery <> getSortedRssUris processedQuery then
                metaRefreshContent (string processedQuery)
            else
                render processedQuery articles

        do! writeChunk context content
        context.Response.OutputStream.Close()
    }

/// Looks up a collection by its id, rendering a "not found" page for an invalid
/// or missing id, otherwise handing the saved feed list to `onFound`.
let private loadCollectionOrNotFound
    (context: HttpListenerContext)
    (collectionId: CollectionId)
    (onFound: string list -> Async<unit>)
    =
    async {
        if not (isValidCollectionId collectionId) then
            do! writeResponse context (collectionNotFoundPage collectionId |> string)
        else
            match tryLoad CollectionsDir collectionId with
            | None -> do! writeResponse context (collectionNotFoundPage collectionId |> string)
            | Some feeds -> do! onFound feeds
    }

let private handleConfigPage (context: HttpListenerContext) =
    async {
        let query = Query.Create context.Request.Url.Query

        match query.GetValues "s" |> List.tryHead with
        | Some rawId ->
            let collectionId = CollectionId rawId

            do!
                loadCollectionOrNotFound context collectionId (fun feeds ->
                    let rssUrls = feeds |> List.map FeedUri.createWithHttps
                    writeResponse context (configPage rssUrls (Some collectionId) |> string))
        | None -> do! writeResponse context (configPage (getRssUrls context.Request.Url.Query) None |> string)
    }

let private handleCreateCollection (context: HttpListenerContext) =
    async {
        let! formData = readFormBody context
        let feeds = formData.GetValues "rss"
        let collectionId = generateCollectionId ()
        save CollectionsDir collectionId feeds
        do! redirectTo context $"/s/{collectionId}"
    }

let private handleUpdateCollection (context: HttpListenerContext) (collectionId: CollectionId) =
    async {
        if isValidCollectionId collectionId then
            let! formData = readFormBody context
            let feeds = formData.GetValues "rss"
            save CollectionsDir collectionId feeds
            do! redirectTo context $"/s/{collectionId}"
        else
            do! writeResponse context (collectionNotFoundPage collectionId |> string)
    }

let private handleViewCollection
    (ctx: AppContext)
    (context: HttpListenerContext)
    (collectionId: CollectionId)
    (shell: Html)
    (content: Query -> Article list -> Html)
    =
    loadCollectionOrNotFound context collectionId (fun feeds ->
        async {
            touch CollectionsDir collectionId
            let collQuery = Query.CreateWithKey("rss", feeds)
            context.Response.SendChunked <- true
            context.Response.ContentType <- "text/html"

            do! writeChunk context shell

            let articles = processRssRequest ctx RequestLogPath (string collQuery)

            do! writeChunk context (content collQuery articles)
            context.Response.OutputStream.Close()
        })

let handleRequest (ctx: AppContext) (context: HttpListenerContext) =
    async {
        ctx.Logger.LogDebug $"Received request {context.Request.Url}"

        match context.Request.RawUrl with
        | Prefix "/config.html" _ -> do! handleConfigPage context
        | Prefix "/shuffle?rss=" _ ->
            let query = Query.Create context.Request.Url.Query

            do! streamFeedResponse ctx context (shuffledFeedsPageShell query) shuffledFeedsPageContent
        | Prefix "/?rss=" _ ->
            let query = Query.Create context.Request.Url.Query

            do! streamFeedResponse ctx context (chronologicalFeedsPageShell query) chronologicalFeedsPageContent
        | "/robots.txt" -> do! writeResponse context (File.ReadAllText(Path.Combine("site", "robots.txt")))
        | "/sitemap.xml" -> do! writeResponse context (File.ReadAllText(Path.Combine("site", "sitemap.xml")))
        | "/s" when context.Request.HttpMethod = "POST" -> do! handleCreateCollection context
        | CollectionShuffleId collectionId when context.Request.HttpMethod = "GET" ->
            do!
                handleViewCollection
                    ctx
                    context
                    collectionId
                    (collectionShuffledPageShell collectionId)
                    shuffledFeedsPageContent
        | CollectionIdPath collectionId when context.Request.HttpMethod = "GET" ->
            do!
                handleViewCollection
                    ctx
                    context
                    collectionId
                    (collectionFeedsPageShell collectionId)
                    chronologicalFeedsPageContent
        | CollectionIdPath collectionId when context.Request.HttpMethod = "POST" ->
            do! handleUpdateCollection context collectionId
        | _ -> do! writeResponse context (landingPage |> string)
    }

let private getCacheAge (logger: ILogger) cacheConfig url =
    let cacheAge =
        OsPath.combine cacheConfig.Dir (url |> convertUrlToValidFilename)
        |> fileLastModified

    match cacheAge with
    | None ->
        logger.LogWarning(
            "No cache file found for {Url}, which is unexpected during a periodic update. Updating cache regardless.",
            url
        )

        Some(PendingFetch(None, url))
    | Some modTime when (DateTimeOffset.Now - modTime) > cacheConfig.Expiration -> Some(PendingFetch(cacheAge, url))
    | _ -> None

let updateCache (ctx: AppContext) (urls: Uri list) =
    if not (List.isEmpty urls) then
        urls
        |> List.choose (getCacheAge ctx.Logger ctx.CacheConfig)
        |> fetchAllRssFeeds ctx.Client ctx.Logger ctx.CacheConfig CacheRefreshFetchConfig
        |> Async.RunSynchronously
        |> List.iter (
            parseFeedResult ctx.Logger
            >> cacheSuccessfulFetch ctx.CacheConfig
            >> feedToArticles
            >> updateMemoryCache ctx.MemCache
            >> ignore
        )

[<TailCall>]
let rec updateRssFeedsPeriodically (ctx: AppContext) =
    async {
        ctx.Logger.LogDebug "Periodically updating RSS feeds."

        uniqueValidRequestLogUrls RequestLogPath |> updateCache ctx

        do! Async.Sleep ctx.CacheConfig.Expiration
        return! updateRssFeedsPeriodically ctx
    }

[<TailCall>]
let rec clearCachePeriodically (logger: ILogger) (cacheDir: OsPath) (retention: TimeSpan) (period: TimeSpan) =
    async {
        logger.LogDebug("Clearing cache files older than {retention} days.", retention.Days)
        clearExpiredCache logger cacheDir retention
        deleteInactive CollectionsDir CollectionRetention

        do! Async.Sleep period
        return! clearCachePeriodically logger cacheDir retention period
    }

let private handleRequestSafely (ctx: AppContext) context =
    async {
        try
            do! handleRequest ctx context
        with ex ->
            ctx.Logger.LogInformation("Request handling error: {Message}", ex.Message)
    }

[<TailCall>]
let rec private serverLoop (listener: HttpListener) (ctx: AppContext) =
    async {
        let! context = listener.GetContextAsync() |> Async.AwaitTask
        do! handleRequestSafely ctx context
        return! serverLoop listener ctx
    }

let startServer (logger: ILogger) (cacheConfig: SimpleRssServer.Config.CacheConfig) (hosts: string list) =
    logger.LogInformation("Starting SimpleRssServer version {version}", version)

    let listener = new HttpListener()
    hosts |> List.iter listener.Prefixes.Add
    listener.Start()
    let addresses = hosts |> String.concat ", "
    logger.LogInformation("Listening at {Addresses}", addresses)

    let ctx =
        { Client = new Http.HttpClient()
          Logger = logger
          CacheConfig = cacheConfig
          MemCache = InMemoryCache logger }

    Async.Start(updateRssFeedsPeriodically ctx)

    let cacheCleanupPeriod = TimeSpan.FromDays 1.0
    Async.Start(clearCachePeriodically logger cacheConfig.Dir CacheRetention cacheCleanupPeriod)

    serverLoop listener ctx

let helpMessage =
    """
Usage: SimpleRssServer [--hostname <url>] [--loglevel <level>]
Options:
  --hostname <url>   Specify the hostname and port (e.g., http://+:5000/)
  --loglevel <level> Set the logging level (debug, info, warning, error)
"""

[<EntryPoint>]
let main argv =
    let parsedArgs = ArgParser.parse (String.concat " " argv)

    match parsedArgs with
    | ArgParser.Help ->
        printfn "%s" helpMessage
        0
    | ArgParser.InvalidArgs msg ->
        eprintfn "Error: %s" msg
        1
    | ArgParser.Args args ->
        let cacheDir = DefaultCacheConfig.Dir

        if not (OsDirectory.exists cacheDir) then
            OsDirectory.create cacheDir

        if not (OsDirectory.exists CollectionsDir) then
            OsDirectory.create CollectionsDir

        let hostname =
            args.Hostname |> Option.defaultValue "http://+:5000/" |> (fun x -> [ x ])

        let logLevel = args.LogLevel |> Option.defaultValue LogLevel.Information
        let logger = initializeLogger logLevel

        startServer logger DefaultCacheConfig hostname |> Async.RunSynchronously
        0
