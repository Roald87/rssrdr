module SimpleRssServer.Logging

open Microsoft.Extensions.Logging

let private loggerFactory =
    LoggerFactory.Create(fun builder ->
        builder.AddSimpleConsole(fun c -> c.TimestampFormat <- "[yyyy-MM-dd HH:mm:ss.fff] ")
        |> ignore

        builder.SetMinimumLevel(LogLevel.Information) |> ignore)

let logger: ILogger = loggerFactory.CreateLogger("SimpleRssReader")
