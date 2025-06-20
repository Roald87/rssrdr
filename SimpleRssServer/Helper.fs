module SimpleRssServer.Helper

open System

// https://stackoverflow.com/a/3722671/6329629
let (|Prefix|_|) (p: string) (s: string) =
    if s.StartsWith p then Some(s.Substring p.Length) else None

let validUris (uris: Result<Uri, string> array) : Uri array =
    uris
    |> Array.choose (function
        | Ok uri -> Some uri
        | Error _ -> None)

let invalidUris (uris: Result<Uri, string> array) : string array =
    uris
    |> Array.choose (function
        | Ok _ -> None
        | Error msg -> Some msg)
