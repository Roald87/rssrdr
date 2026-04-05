open Microsoft.Extensions.Logging
open System
open System.IO
open System.Net
open System.Text

open SimpleRssServer.Cache
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

let processRssRequest client cacheConfig (memCache: InMemoryCache) (logPath: OsPath) (query: string) =
    let readCache = readFromCache cacheConfig memCache

    getRssUrls query
    |> Array.map (toUriProcessState >> readCache) // try read cache before first fetch
    |> fetchAllRssFeeds client logger cacheConfig
    |> Async.RunSynchronously
    |> Array.map (readCache >> parseFeedResult logger) // read from cache in case of 304 Not modified
    |> Array.collect checkIfDiscoveryFeeds
    |> Array.map readCache // read discovered feeds from cache
    |> fetchAllRssFeeds client logger cacheConfig
    |> Async.RunSynchronously
    |> Array.map (
        readCache // previous fetch can contain 304s
        >> parseFeedResult logger
        >> cacheSuccessfulFetch cacheConfig
    )
    |> logSuccessfulFeedRequestsAndParses logPath
    |> Array.map (feedToArticles >> updateMemoryCache memCache)
    |> Array.collect onlyFeedArticles

let getFeedUrlQuery articles =
    articles
    |> Array.map (fun a -> a.FeedUrl)
    |> Array.distinct
    |> fun u -> Query.CreateWithKey("rss", u)

let handleRequest client (cacheConfig: CacheConfig) (memCache: InMemoryCache) (context: HttpListenerContext) =
    async {
        logger.LogDebug $"Received request {context.Request.Url}"

        let getRssArticles () =
            processRssRequest client cacheConfig memCache RequestLogPath context.Request.Url.Query

        let getSortedRssUris (q: Query) =
            q.GetValues "rss" |> Option.ofObj |> Option.defaultValue [||] |> Array.sort

        let buildFeedResponse render =
            let rssArticles = getRssArticles ()
            let procesedQuery = getFeedUrlQuery rssArticles
            let originalQuery = Query.Create context.Request.Url.Query

            if getSortedRssUris originalQuery <> getSortedRssUris procesedQuery then
                context.Response.StatusCode <- HttpStatusCode.Found |> int
                context.Response.RedirectLocation <- procesedQuery.ToString()

            render procesedQuery rssArticles |> string

        let! responseString =
            match context.Request.RawUrl with
            | Prefix "/config.html" _ -> async.Return(getRssUrls context.Request.Url.Query |> configPage |> string)
            | Prefix "/shuffle?rss=" _ -> async.Return(buildFeedResponse shuffledFeedsPage)
            | Prefix "/?rss=" _ -> async.Return(buildFeedResponse chronologicalFeedsPage)
            | "/robots.txt" -> async.Return(File.ReadAllText(Path.Combine("site", "robots.txt")))
            | "/sitemap.xml" -> async.Return(File.ReadAllText(Path.Combine("site", "sitemap.xml")))
            | _ -> async.Return(landingPage |> string)

        let buffer = responseString |> Encoding.UTF8.GetBytes
        context.Response.ContentLength64 <- int64 buffer.Length
        context.Response.ContentType <- "text/html"

        do!
            context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length)
            |> Async.AwaitTask

        context.Response.OutputStream.Close()
    }

let private getCacheAge cacheConfig url =
    let cacheAge =
        Path.Combine(cacheConfig.Dir, url |> convertUrlToValidFilename)
        |> fileLastModified

    match cacheAge with
    | None ->
        logger.LogWarning(
            "No cache file found for {Url}, which is unexpected during a periodic update. Updating cache regardless.",
            url
        )

        Some(ValidUri(None, url))
    | Some modTime when (DateTimeOffset.Now - modTime) > cacheConfig.Expiration -> Some(ValidUri(cacheAge, url))
    | _ -> None

let updateCache client cacheConfig (memCache: InMemoryCache) (urls: Uri array) =
    if urls.Length > 0 then
        urls
        |> Array.choose (getCacheAge cacheConfig)
        |> fetchAllRssFeeds client logger cacheConfig
        |> Async.RunSynchronously
        |> Array.map (
            parseFeedResult logger
            >> cacheSuccessfulFetch cacheConfig
            >> feedToArticles
            >> updateMemoryCache memCache
        )
        |> ignore

let updateRssFeedsPeriodically client (cacheConfig: CacheConfig) (memCache: InMemoryCache) =
    async {
        while true do
            logger.LogDebug "Periodically updating RSS feeds."

            uniqueValidRequestLogUrls RequestLogPath
            |> updateCache client cacheConfig memCache

            do! Async.Sleep cacheConfig.Expiration
    }

let clearCachePeriodically (cacheDir: OsPath) (retention: TimeSpan) (period: TimeSpan) =
    async {
        while true do
            logger.LogDebug("Clearing cache files older than {retention} days.", retention.Days)
            clearExpiredCache cacheDir retention

            do! Async.Sleep period
    }

let startServer (cacheConfig: SimpleRssServer.Config.CacheConfig) (hosts: string list) =
    let listener = new HttpListener()
    hosts |> List.iter listener.Prefixes.Add
    listener.Start()
    let addresses = hosts |> String.concat ", "
    logger.LogInformation("Listening at {Addresses}", addresses)

    let httpClient = new Http.HttpClient()
    let feedCache = InMemoryCache logger

    let rec loop () =
        async {
            let! context = listener.GetContextAsync() |> Async.AwaitTask

            try
                do! handleRequest httpClient cacheConfig feedCache context
            with ex ->
                logger.LogInformation("Request handling error: {Message}", ex.Message)

            return! loop ()
        }

    Async.Start(updateRssFeedsPeriodically httpClient cacheConfig feedCache)

    let cacheCleanupPeriod = TimeSpan.FromDays 1.0
    Async.Start(clearCachePeriodically cacheConfig.Dir CacheRetention cacheCleanupPeriod)

    loop ()

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
    | ArgParser.Args args ->
        let cacheDir = DefaultCacheConfig.Dir

        if not (Directory.Exists cacheDir) then
            Directory.CreateDirectory cacheDir |> ignore

        let hostname =
            args.Hostname |> Option.defaultValue "http://+:5000/" |> (fun x -> [ x ])

        let logLevel = args.LogLevel |> Option.defaultValue LogLevel.Information
        initializeLogger logLevel |> ignore

        startServer DefaultCacheConfig hostname |> Async.RunSynchronously
        0
