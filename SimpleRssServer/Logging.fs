module SimpleRssServer.Logging

open Microsoft.Extensions.Logging

let createLoggerFactory (logLevel: LogLevel) =
    LoggerFactory.Create(fun builder ->
        builder.AddSimpleConsole(fun c -> c.TimestampFormat <- "[yyyy-MM-dd HH:mm:ss.fff] ")
        |> ignore

        builder.SetMinimumLevel(logLevel) |> ignore)

let mutable logger: ILogger = Unchecked.defaultof<ILogger>

let initializeLogger verbosity =
    logger <- (createLoggerFactory verbosity).CreateLogger "SimpleRssReader"
