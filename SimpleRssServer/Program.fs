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
open SimpleRssServer.Request
open SimpleRssServer.RequestLog
open SimpleRssServer.RssParser
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.DomainModel

type FeedOrder =
    | Chronological
    | Random

let assembleRssFeeds (logger: ILogger) order client cacheConfig rssUris =
    let items = rssUris |> validUris |> fetchAllRssFeeds client cacheConfig

    let okUris =
        rssUris |> validUris |> Array.map (fun u -> u.AbsoluteUri) |> Set.ofArray

    let failedResponse =
        items
        |> Seq.choose (function
            | Ok _ -> None
            | Error e -> e.UriOption)
        |> Set.ofSeq

    let successResponses =
        Set.difference okUris failedResponse |> Set.toArray |> Array.map Uri

    let invalidUris =
        rssUris
        |> Array.choose (function
            | Error(UriError.HostNameMustContainDot e) -> Some(Error(UriHostNameMustContainDot e))
            | Error(UriError.UriFormatException(e, ex)) -> Some(Error(UriFormatException(e, ex)))
            | Ok _ -> None)

    let allItems = Array.append items invalidUris |> Seq.collect (parseRss logger)

    let rssQuery =
        rssUris
        |> validUris
        |> Array.map (fun u -> u.AbsoluteUri.Replace("https://", ""))
        |> String.concat "&rss="

    let query = if rssQuery.Length > 0 then $"?rss={rssQuery}" else rssQuery

    match order with
    | Chronological -> successResponses, homepage query allItems
    | Random -> successResponses, randomPage query allItems

let handleRequest client (cacheConfig: CacheConfig) (context: HttpListenerContext) =
    async {
        logger.LogDebug $"Received request {context.Request.Url}"

        let rssUris = getRssUrls context.Request.Url.Query

        let responseString =
            match context.Request.RawUrl with
            | Prefix "/config.html" _ -> configPage rssUris |> string
            | Prefix "/random?rss=" _ ->
                let okRequests, page = assembleRssFeeds logger Random client cacheConfig rssUris
                updateRequestLog RequestLogPath RequestLogRetention okRequests
                page |> string
            | Prefix "/?rss=" _ ->
                let okRequests, page =
                    assembleRssFeeds logger Chronological client cacheConfig rssUris

                updateRequestLog RequestLogPath RequestLogRetention okRequests
                page |> string
            | "/robots.txt" -> File.ReadAllText(Path.Combine("site", "robots.txt"))
            | "/sitemap.xml" -> File.ReadAllText(Path.Combine("site", "sitemap.xml"))
            | _ -> landingPage |> string

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
            let urls = readRequestLog RequestLogPath

            if urls.Length > 0 then
                logger.LogDebug $"Periodically updating {urls.Length} RSS feeds."
                fetchAllRssFeeds client cacheConfig urls |> ignore

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
