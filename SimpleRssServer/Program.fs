open Microsoft.Extensions.Logging
open System.IO
open System.Net

open SimpleRssServer.Logging
open SimpleRssServer.Request

type Millisecond = Millisecond of int

let updateRssFeedsPeriodically client cacheDir (period: Millisecond) =
    async {
        while true do
            let urls = requestUrls requestLogPath

            if urls.Length > 0 then
                logger.LogDebug $"Periodically updating {urls.Length} RSS feeds."
                fetchAllRssFeeds client cacheDir urls |> ignore

            let (Millisecond t) = period
            do! Async.Sleep(t)
    }

let startServer cacheDir (prefixes: string list) =
    let listener = new HttpListener()
    prefixes |> List.iter listener.Prefixes.Add
    listener.Start()
    let addresses = prefixes |> String.concat ", "
    logger.LogInformation("Listening at {Addresses}", addresses)

    let httpClient = new Http.HttpClient()

    let rec loop () =
        async {
            let! context = listener.GetContextAsync() |> Async.AwaitTask
            do! handleRequest httpClient cacheDir context
            return! loop ()
        }

    let oneHour = Millisecond(1000 * 60 * 60)
    Async.Start(updateRssFeedsPeriodically httpClient cacheDir oneHour)
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
        let cacheDir = "rss-cache"

        if not (Directory.Exists cacheDir) then
            Directory.CreateDirectory cacheDir |> ignore

        let hostname =
            args.Hostname |> Option.defaultValue "http://+:5000/" |> fun x -> [ x ]

        let logLevel = args.Loglevel |> Option.defaultValue LogLevel.Information
        initializeLogger logLevel |> ignore

        startServer cacheDir hostname |> Async.RunSynchronously
        0
