open Microsoft.Extensions.Logging
open System
open System.IO
open System.Net
open System.Text

open Roald87.FeedReader
open SimpleRssServer.Cache
open SimpleRssServer.Config
open SimpleRssServer.Helper
open SimpleRssServer.HtmlRenderer
open SimpleRssServer.Logging
open SimpleRssServer.Request
open SimpleRssServer.RequestLog
open SimpleRssServer.RssParser
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes

type FeedOrder =
    | Chronological
    | Random

type AssembleResult =
    | FeedsReady of Uri[] * Html
    | NeedsSelection of confirmedRss: Uri[] * toSelect: DiscoveredFeed list

type private FetchParseResult =
    | ValidFeed of Uri * Article list
    | MultiDiscovered of DiscoveredFeed list
    | ErrorArticles of Article list

let private toDiscoveredFeed (pageUri: Uri) (link: HtmlFeedLink) : DiscoveredFeed =
    let absoluteLink = FeedReader.GetAbsoluteFeedUrl(pageUri.ToString(), link)
    let url = absoluteLink.Url

    let title =
        if String.IsNullOrWhiteSpace absoluteLink.Title then
            url
        else
            absoluteLink.Title

    { Title = title; Url = url }

let private parseSingleDiscoveredFeed (logger: ILogger) cacheConfig (fetchResult: FetchResult) =
    async {
        match fetchResult with
        | FreshContent(content, uri) ->
            match tryParseFeed logger content uri with
            | Ok feed ->
                do! cacheSuccessfulFetch cacheConfig uri content
                return ValidFeed(uri, feedToArticles feed)
            | Error err -> return ErrorArticles [ createErrorArticle err ]
        | other -> return ErrorArticles(parseRss logger other)
    }

let private discoverFeeds (logger: ILogger) client cacheConfig (content: string) (uri: Uri) (err: DomainMessage) =
    async {
        let htmlFeedLinks = FeedReader.ParseFeedUrlsFromHtml(content) |> Seq.toList

        match htmlFeedLinks with
        | [] -> return ErrorArticles [ createErrorArticle err ]
        | [ link ] ->
            let absoluteUrl = (toDiscoveredFeed uri link).Url
            let! fetchResult = fetchUrlWithCacheAsync client cacheConfig (Uri.Create absoluteUrl)
            return! parseSingleDiscoveredFeed logger cacheConfig fetchResult
        | _ -> return MultiDiscovered(htmlFeedLinks |> List.map (toDiscoveredFeed uri))
    }

let assembleRssFeeds (logger: ILogger) order client cacheConfig rssUris =
    async {
        let! rssFeeds = fetchAllRssFeeds client cacheConfig rssUris

        let allValidUris = rssUris |> validUris |> Array.map (fun u -> u.AbsoluteUri)

        let query =
            allValidUris
            |> Array.map (fun s -> s.Replace("https://", ""))
            |> String.concat "&rss="
            |> fun s -> if s.Length > 0 then $"?rss={s}" else s

        let! parsedResults =
            rssFeeds
            |> Array.map (fun fetchResult ->
                async {
                    match fetchResult with
                    | FreshContent(content, uri) ->
                        match tryParseFeed logger content uri with
                        | Ok feed ->
                            do! cacheSuccessfulFetch cacheConfig uri content
                            return ValidFeed(uri, feedToArticles feed)
                        | Error err -> return! discoverFeeds logger client cacheConfig content uri err
                    | other -> return ErrorArticles(parseRss logger other)
                })
            |> Async.Parallel

        let multiDiscoveredLinks =
            parsedResults
            |> Seq.choose (function
                | MultiDiscovered links -> Some links
                | _ -> None)
            |> Seq.concat
            |> Seq.toList

        let confirmedUris =
            parsedResults
            |> Seq.choose (function
                | ValidFeed(uri, _) -> Some uri
                | _ -> None)
            |> Seq.toArray

        if multiDiscoveredLinks.IsEmpty then
            let allItems =
                parsedResults
                |> Seq.collect (function
                    | ValidFeed(_, articles) -> articles
                    | ErrorArticles articles -> articles
                    | MultiDiscovered _ -> [])

            let page =
                match order with
                | Chronological -> homepage query allItems
                | Random -> randomPage query allItems

            return FeedsReady(confirmedUris, page)
        else
            return NeedsSelection(confirmedUris, multiDiscoveredLinks)
    }

let handleRequest client (cacheConfig: CacheConfig) (context: HttpListenerContext) =
    async {
        logger.LogDebug $"Received request {context.Request.Url}"

        let rssUris = getRssUrls context.Request.Url.Query

        let! responseString =
            match context.Request.RawUrl with
            | Prefix "/config.html" _ -> async { return configPage rssUris |> string }
            | Prefix "/random?rss=" _ ->
                async {
                    let! result = assembleRssFeeds logger Random client cacheConfig rssUris

                    match result with
                    | FeedsReady(okRequests, page) ->
                        updateRequestLog RequestLogPath RequestLogRetention okRequests
                        return page |> string
                    | NeedsSelection(confirmed, toSelect) -> return feedDiscoveryPage confirmed toSelect |> string
                }
            | Prefix "/?rss=" _ ->
                async {
                    let! result = assembleRssFeeds logger Chronological client cacheConfig rssUris

                    match result with
                    | FeedsReady(okRequests, page) ->
                        updateRequestLog RequestLogPath RequestLogRetention okRequests
                        return page |> string
                    | NeedsSelection(confirmed, toSelect) -> return feedDiscoveryPage confirmed toSelect |> string
                }
            | "/robots.txt" -> async { return File.ReadAllText(Path.Combine("site", "robots.txt")) }
            | "/sitemap.xml" -> async { return File.ReadAllText(Path.Combine("site", "sitemap.xml")) }
            | _ -> async { return landingPage |> string }

        let buffer = responseString |> Encoding.UTF8.GetBytes
        context.Response.ContentLength64 <- int64 buffer.Length
        context.Response.ContentType <- "text/html"

        do!
            context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length)
            |> Async.AwaitTask

        context.Response.OutputStream.Close()
    }

let updateRssFeedsPeriodically client (cacheConfig: SimpleRssServer.Config.CacheConfig) =
    async {
        while true do
            let urls = readRequestLog RequestLogPath |> Array.map Ok

            if urls.Length > 0 then
                logger.LogDebug $"Periodically updating {urls.Length} RSS feeds."
                do! fetchAllRssFeeds client cacheConfig urls |> Async.Ignore

            do! Async.Sleep cacheConfig.Expiration
    }

let clearCachePeriodically (cacheDir: OsPath) (retention: TimeSpan) (period: TimeSpan) =
    async {
        while true do
            logger.LogDebug "Clearing expired cache files (older than 7 days)."
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

    let rec loop () =
        async {
            let! context = listener.GetContextAsync() |> Async.AwaitTask
            do! handleRequest httpClient cacheConfig context
            return! loop ()
        }

    Async.Start(updateRssFeedsPeriodically httpClient cacheConfig)

    // Run cache cleanup once per day (24 hours = 86400000 ms)
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
