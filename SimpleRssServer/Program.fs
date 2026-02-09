open Microsoft.Extensions.Logging
open System.IO
open System.Net

open SimpleRssServer.Logging
open SimpleRssServer.Request
open SimpleRssServer.RequestLog
open SimpleRssServer.Cache
open SimpleRssServer.Config

type Millisecond = Millisecond of int

let updateRssFeedsPeriodically client (cacheConfig: SimpleRssServer.Config.CacheConfig) (period: Millisecond) =
    async {
        while true do
            let urls = readRequestLog RequestLogPath

            if urls.Length > 0 then
                logger.LogDebug $"Periodically updating {urls.Length} RSS feeds."
                fetchAllRssFeeds client cacheConfig urls |> ignore

            let (Millisecond t) = period
            do! Async.Sleep t
    }

let clearCachePeriodically cacheDir retention (period: Millisecond) =
    async {
        while true do
            logger.LogDebug "Clearing expired cache files (older than 7 days)."
            do! clearExpiredCache cacheDir retention

            let (Millisecond t) = period
            do! Async.Sleep t
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

    let cacheExpirationPeriod =
        Millisecond(cacheConfig.ExpirationHours * 1000.0 * 60.0 * 60.0 |> int)

    Async.Start(updateRssFeedsPeriodically httpClient cacheConfig cacheExpirationPeriod)

    // Run cache cleanup once per day (24 hours = 86400000 ms)
    let cacheCleanupPeriod = Millisecond(24 * 60 * 60 * 1000)
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
