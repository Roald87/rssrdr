module ArgParser

type ParsedArgs = { Argument: string }

let parse (args: string) : ParsedArgs = { Argument = args }
