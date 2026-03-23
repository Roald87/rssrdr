module SimpleRssServer.Helper

open System
open DomainPrimitiveTypes

// https://stackoverflow.com/a/3722671/6329629
let (|Prefix|_|) (p: string) (s: string) =
    if s.StartsWith p then Some(s.Substring p.Length) else None

let validUris (uris: Result<Uri, UriError> array) : Uri array = uris |> Array.choose Result.toOption

let isText (s: string) = not (String.IsNullOrWhiteSpace s)
