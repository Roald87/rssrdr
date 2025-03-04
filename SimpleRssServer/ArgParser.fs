module ArgParser

open Microsoft.Extensions.Logging

type Args = {
    Hostname: string option
    Loglevel: LogLevel option
}

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
        | "--loglevel" :: "debug" :: rest -> parseArgs rest { acc with Loglevel = Some LogLevel.Debug }
        | "--loglevel" :: "info" :: rest -> parseArgs rest { acc with Loglevel = Some LogLevel.Information }
        | "--loglevel" :: "warning" :: rest -> parseArgs rest { acc with Loglevel = Some LogLevel.Warning }
        | "--loglevel" :: "error" :: rest -> parseArgs rest { acc with Loglevel = Some LogLevel.Error }
        | "--loglevel" :: invalid :: _ -> failwith $"Loglevel {invalid} does not exist"
        | _ -> acc
    parseArgs (List.ofArray parts) { Hostname = None; Loglevel = None }
