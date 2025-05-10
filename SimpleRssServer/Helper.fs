module SimpleRssServer.Helper

type Result<'TSuccess, 'TFailure> =
    | Success of 'TSuccess
    | Failure of 'TFailure

// https://stackoverflow.com/a/3722671/6329629
let (|Prefix|_|) (p: string) (s: string) =
    if s.StartsWith p then Some(s.Substring p.Length) else None
