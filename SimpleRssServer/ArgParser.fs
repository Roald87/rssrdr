module ArgParser

open Microsoft.Extensions.Logging

type Args =
    { Hostname: string option
      LogLevel: LogLevel option }

type ParsedArgs =
    | Args of Args
    | Help

let parse (args: string) : ParsedArgs =
    let parts = args.Split(' ')

    let rec parseArgs parts acc =
        match parts with
        | [] -> Args acc
        | "--help" :: _ -> Help
        | "--hostname" :: hostname :: rest -> parseArgs rest { acc with Hostname = Some hostname }
        | "--loglevel" :: "debug" :: rest ->
            parseArgs
                rest
                { acc with
                    LogLevel = Some LogLevel.Debug }
        | "--loglevel" :: "info" :: rest ->
            parseArgs
                rest
                { acc with
                    LogLevel = Some LogLevel.Information }
        | "--loglevel" :: "warning" :: rest ->
            parseArgs
                rest
                { acc with
                    LogLevel = Some LogLevel.Warning }
        | "--loglevel" :: "error" :: rest ->
            parseArgs
                rest
                { acc with
                    LogLevel = Some LogLevel.Error }
        | "--loglevel" :: invalid :: _ -> failwith $"Log level {invalid} does not exist"
        | _ -> Args acc

    parseArgs (List.ofArray parts) { Hostname = None; LogLevel = None }
