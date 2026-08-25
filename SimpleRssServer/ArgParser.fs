module ArgParser

open Microsoft.Extensions.Logging

type Args =
    { Hostname: string option
      LogLevel: LogLevel option }

type ParsedArgs =
    | Args of Args
    | Help
    | InvalidArgs of string

let private logLevels =
    Map
        [ "debug", LogLevel.Debug
          "info", LogLevel.Information
          "warning", LogLevel.Warning
          "error", LogLevel.Error ]

[<TailCall>]
let rec private parseArgs parts acc =
    match parts with
    | [] -> Args acc
    | "--help" :: _ -> Help
    | "--hostname" :: hostname :: rest -> parseArgs rest { acc with Hostname = Some hostname }
    | "--loglevel" :: level :: rest ->
        match logLevels.TryFind level with
        | Some logLevel -> parseArgs rest { acc with LogLevel = Some logLevel }
        | None -> InvalidArgs $"Log level {level} does not exist"
    | _ -> Args acc

let parse (args: string) : ParsedArgs =
    let parts = args.Split ' '
    parseArgs (List.ofArray parts) { Hostname = None; LogLevel = None }
