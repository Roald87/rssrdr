module ArgParser

type ParsedArgs = { Hostname: string option }

let parse (args: string) : ParsedArgs =
    let parts = args.Split(' ')
    match parts with
    | [| "--hostname"; hostname |] -> { Hostname = Some hostname }
    | _ -> { Hostname = None }
