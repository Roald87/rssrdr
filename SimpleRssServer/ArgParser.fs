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
    match parts with
    | [| "--help" |] -> Help
    | [| "--hostname"; hostname |] -> Args { Hostname = Some hostname; Loglevel = None }
    | [| "--loglevel"; "debug" |] -> Args { Hostname = None; Loglevel = Some LogLevel.Debug }
    | [| "--loglevel"; "info" |] -> Args { Hostname = None; Loglevel = Some LogLevel.Information }
    | [| "--loglevel"; "warning" |] -> Args { Hostname = None; Loglevel = Some LogLevel.Warning }
    | [| "--loglevel"; "error" |] -> Args { Hostname = None; Loglevel = Some LogLevel.Error }
    | [| "--loglevel"; invalid |] -> failwith $"Loglevel {invalid} does not exist"
    | _ -> Args(None, None)
