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

let readFromCache (cacheConfig: CacheConfig) (ups: UriProcessState) : UriProcessState =
    match ups with
    | ValidUri(_, u) ->
        let cachePath = Path.Combine(cacheConfig.Dir, convertUrlToValidFilename u)
        let cacheModified = fileLastModified cachePath

        match cacheModified with
        | None -> ValidUri(None, u)
        | Some modTime when (DateTimeOffset.Now - modTime) <= cacheConfig.Expiration ->
            match readCache cachePath with
            | Some s -> CachedFeed(s, u)
            | None -> ValidUri(None, u)
        | Some modTime -> ValidUri(Some modTime, u)
    | ProcessingError e ->
        match e.Uri with
        | Some uriStr ->
            let feedUri = Uri uriStr
            let cachePath = Path.Combine(cacheConfig.Dir, convertUrlToValidFilename feedUri)

            match readCache cachePath with
            | Some content -> StaleHitWithError(content, feedUri, e)
            | None -> ProcessingError e
        | None -> ProcessingError e
    | _ -> ups

let toUriProcessState (uri: Result<Uri, UriError>) : UriProcessState =
    match uri with
    | Ok u -> ValidUri(Some DateTimeOffset.Now, u)
    | Error u ->
        match u with
        | HostNameMustContainDot iu -> ProcessingError(InvalidUriHostname iu)
        | UriFormatException(iu, ex) -> ProcessingError(InvalidUriFormat(iu, ex))

let parseFeedResult (logger: ILogger) (ups: UriProcessState) =
    match ups with
    | Response(r, feedUri) ->
        match tryParseFeed logger r feedUri with
        | Ok f -> ParsedFeed(UnparsedXml r, f)
        | Error e ->
            match e with
            | InvalidRssFeedFormat _ -> ResponseCanContainsFeeds(r, feedUri)
            | _ -> ProcessingError e
    | CachedFeed(r, feedUri) ->
        match tryParseFeed logger r feedUri with
        | Ok f -> ParsedCachedFeed f
        | Error e -> ProcessingError e
    | StaleHitWithError(r, feedUri, err) ->
        match tryParseFeed logger r feedUri with
        | Ok f -> ParsedStaleHit(f, err)
        | Error _ -> ProcessingError err
    | _ -> ups


let checkIfDiscoveryFeeds ups =
    match ups with
    | ResponseCanContainsFeeds(s, originalUri) ->
        let feed = FeedReader.ParseFeedUrlsFromHtml s |> Seq.toArray

        match feed with
        | [||] -> [| ProcessingError(InvalidRssFeedFormat(originalUri, Exception "No RSS feeds found in page")) |]
        | x -> x |> Array.map (fun u -> ValidUri(None, Uri u.Url))
    | x -> [| x |]


let cacheSuccessfulFetch cacheConfig ups =
    match ups with
    | ParsedFeed(xml, feed) ->
        let cachePath =
            Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feed.Link))

        writeCache cachePath xml.Value
    | _ -> ()

    ups

let onlyFeedArticles ups =
    match ups with
    | FeedArticles articles -> articles
    | _ -> [||]

let logSuccessfulFeedRequestsAndParses (logPath: OsPath) (upss: UriProcessState array) =
    upss
    |> Array.choose (function
        | ParsedFeed(_, feed) -> Some(Uri feed.Link)
        | ParsedCachedFeed feed -> Some(Uri feed.Link)
        | _ -> None)
    |> updateRequestLog logPath RequestLogRetention

    upss

let processRssRequest client cacheConfig (logPath: OsPath) (query: string) =
    getRssUrls query
    |> Array.map (toUriProcessState >> readFromCache cacheConfig) // try read cache before first fetch
    |> fetchAllRssFeeds client logger cacheConfig
    |> Async.RunSynchronously
    |> Array.map (readFromCache cacheConfig >> parseFeedResult logger) // read from cache in case of 304 Not modified
    |> Array.collect checkIfDiscoveryFeeds
    |> Array.map (readFromCache cacheConfig) // read discovered feeds from cache
    |> fetchAllRssFeeds client logger cacheConfig
    |> Async.RunSynchronously
    |> Array.map (
        readFromCache cacheConfig // previous fetch could again return 304
        >> parseFeedResult logger
        >> cacheSuccessfulFetch cacheConfig
    )
    |> logSuccessfulFeedRequestsAndParses logPath
    |> Array.map feedToArticles
    |> Array.collect onlyFeedArticles
    |> Array.toSeq

let handleRequest client (cacheConfig: CacheConfig) (context: HttpListenerContext) =
    async {
        logger.LogDebug $"Received request {context.Request.Url}"

        let rssUris = getRssUrls context.Request.Url.Query

        let processRssRequest =
            processRssRequest client cacheConfig RequestLogPath context.Request.Url.Query

        // TODO this probably needs to come from the processRssRequest, such that it filters out old stuff and removes invalid urls
        let query = Query.Create context.Request.Url.Query

        let! responseString =
            match context.Request.RawUrl with
            | Prefix "/config.html" _ -> async.Return(configPage rssUris |> string)
            | Prefix "/shuffle?rss=" _ -> async.Return(shuffledFeedsPage query processRssRequest |> string)
            | Prefix "/?rss=" _ -> async.Return(chronologicalFeedsPage query processRssRequest |> string)
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

let updateCache client cacheConfig (urls: Uri array) =
    if urls.Length > 0 then
        urls
        |> Array.choose (getCacheAge cacheConfig)
        |> fetchAllRssFeeds client logger cacheConfig
        |> Async.RunSynchronously
        |> Array.map (parseFeedResult logger)
        |> Array.iter (cacheSuccessfulFetch cacheConfig >> ignore)

let updateRssFeedsPeriodically client (cacheConfig: CacheConfig) =
    async {
        while true do
            logger.LogDebug "Periodically updating RSS feeds."

            uniqueValidRequestLogUrls RequestLogPath |> updateCache client cacheConfig

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
