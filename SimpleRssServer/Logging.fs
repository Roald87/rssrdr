module SimpleRssServer.Logging

open Microsoft.Extensions.Logging

let initializeLogger (logLevel: LogLevel) : ILogger =
    let logFactory =
        LoggerFactory.Create(fun builder ->
            builder.AddSimpleConsole(fun c -> c.TimestampFormat <- "[yyyy-MM-dd HH:mm:ss.fff] ").SetMinimumLevel
                logLevel
            |> ignore)

    logFactory.CreateLogger "SimpleRssReader"
