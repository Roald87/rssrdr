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
    | _ -> { Hostname = None; Loglevel = None }
