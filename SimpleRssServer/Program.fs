open Microsoft.Extensions.Logging
open System
open System.Net

open SimpleRssServer.AppContext
open SimpleRssServer.Cache
open SimpleRssServer.Collections
open SimpleRssServer.Config
open SimpleRssServer.Logging
open SimpleRssServer.MemoryCache
open SimpleRssServer.Request
open SimpleRssServer.RequestHandlers
open SimpleRssServer.RequestLog
open SimpleRssServer.RssParser
open SimpleRssServer.DomainPrimitiveTypes

let updateCache (appCtx: AppContext) (urls: Uri list) =
    if not (List.isEmpty urls) then
        urls
        |> List.choose (getCacheAge appCtx.Logger appCtx.CacheConfig)
        |> List.map (checkIfInBackoff appCtx.Logger appCtx.CacheConfig)
        |> fetchRssFeeds appCtx.Client appCtx.Logger appCtx.CacheConfig CacheRefreshFetchConfig
        |> Async.RunSynchronously
        |> List.iter (
            parseFeedResult appCtx.Logger
            >> cacheSuccessfulFetch appCtx.CacheConfig
            >> feedToArticles
            >> updateMemoryCache appCtx.MemCache
            >> ignore
        )

[<TailCall>]
let rec clearPersistentCachePeriodically (appCtx: AppContext) (retention: TimeSpan) (period: TimeSpan) =
    async {
        appCtx.Logger.LogDebug("Clearing cache files older than {retention} days.", retention.Days)
        clearExpiredCache appCtx.Logger appCtx.CacheConfig.Dir retention
        deleteInactive appCtx.CollectionsDir CollectionRetention

        do! Async.Sleep period
        return! clearPersistentCachePeriodically appCtx retention period
    }

[<TailCall>]
let rec updateRssFeedsPeriodically (appCtx: AppContext) (period: TimeSpan) =
    async {
        appCtx.Logger.LogDebug "Periodically updating RSS feeds."

        uniqueValidRequestLogUrls appCtx.RequestLogPath |> updateCache appCtx

        do! Async.Sleep period
        return! updateRssFeedsPeriodically appCtx period
    }

[<TailCall>]
let rec private serverLoop (listener: HttpListener) (appCtx: AppContext) =
    async {
        let! httpCtx = listener.GetContextAsync() |> Async.AwaitTask
        do! handleRequestSafely appCtx httpCtx
        return! serverLoop listener appCtx
    }

let startServer (logger: ILogger) (cacheConfig: SimpleRssServer.Config.CacheConfig) (hosts: string list) =
    logger.LogInformation("Starting SimpleRssServer version {version}", version)

    let listener = new HttpListener()
    hosts |> List.iter listener.Prefixes.Add
    listener.Start()
    let addresses = hosts |> String.concat ", "
    logger.LogInformation("Listening at {Addresses}", addresses)

    let appCtx =
        { Client = new Http.HttpClient()
          Logger = logger
          CacheConfig = cacheConfig
          MemCache = InMemoryCache logger
          CollectionsDir = CollectionsDir
          RequestLogPath = RequestLogPath }

    Async.Start(updateRssFeedsPeriodically appCtx cacheConfig.Expiration)
    Async.Start(clearPersistentCachePeriodically appCtx CacheRetention CacheCleanupPeriod)

    serverLoop listener appCtx

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
