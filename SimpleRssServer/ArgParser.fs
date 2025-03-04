module ArgParser

open Microsoft.Extensions.Logging

type ParsedArgs =
    | Args of Hostname: string option * Loglevel: LogLevel option
    | Help

let parse (args: string) : ParsedArgs =
    let parts = args.Split(' ')
    match parts with
    | [| "--help" |] -> Help
    | [| "--hostname"; hostname |] -> Args(Some hostname, None)
    | [| "--loglevel"; "debug" |] -> Args(None, Some LogLevel.Debug)
    | [| "--loglevel"; "info" |] -> Args(None, Some LogLevel.Information)
    | [| "--loglevel"; "warning" |] -> Args(None, Some LogLevel.Warning)
    | [| "--loglevel"; "error" |] -> Args(None, Some LogLevel.Error)
    | [| "--loglevel"; invalid |] -> failwith $"Loglevel {invalid} does not exist"
    | _ -> Args(None, None)
