module ArgParser

open Microsoft.Extensions.Logging

type ParsedArgs = { 
    Hostname: string option
    Loglevel: LogLevel option
    }

let parse (args: string) : ParsedArgs =
    let parts = args.Split(' ')
    match parts with
    | [| "--hostname"; hostname |] -> { Hostname = Some hostname; Loglevel = None }
    | [| "--loglevel"; "debug" |] -> { Hostname = None; Loglevel = Some LogLevel.Debug }
    | [| "--loglevel"; "info" |] -> { Hostname = None; Loglevel = Some LogLevel.Information }
    | [| "--loglevel"; "warning" |] -> { Hostname = None; Loglevel = Some LogLevel.Warning }
    | [| "--loglevel"; "error" |] -> { Hostname = None; Loglevel = Some LogLevel.Error }
    | [| "--loglevel"; invalid |] -> failwith $"Loglevel {invalid} does not exist"
    | _ -> { Hostname = None; Loglevel = None }
