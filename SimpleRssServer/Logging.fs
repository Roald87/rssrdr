module SimpleRssServer.Logging

open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions

let mutable logger: ILogger = NullLogger.Instance

let initializeLogger (logLevel: LogLevel) =
    let logFactory =
        LoggerFactory.Create(fun builder ->
            builder.AddSimpleConsole(fun c -> c.TimestampFormat <- "[yyyy-MM-dd HH:mm:ss.fff] ").SetMinimumLevel
                logLevel
            |> ignore)

    logger <- logFactory.CreateLogger "SimpleRssReader"
