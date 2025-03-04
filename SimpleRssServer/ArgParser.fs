module ArgParser

type ParsedArgs = { Hostname: string option }

let parse (args: string) : ParsedArgs = { Hostname = Some args }
