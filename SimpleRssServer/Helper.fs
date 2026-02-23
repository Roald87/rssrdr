module SimpleRssServer.Helper

open System
open DomainPrimitiveTypes

// https://stackoverflow.com/a/3722671/6329629
let (|Prefix|_|) (p: string) (s: string) =
    if s.StartsWith p then Some(s.Substring p.Length) else None

let validUris (uris: Result<Uri, UriError> array) : Uri array =
    uris
    |> Array.choose (function
        | Ok uri -> Some uri
        | Error _ -> None)

let invalidUris (uris: Result<Uri, UriError> array) : string array =
    uris
    |> Array.choose (function
        | Ok _ -> None
        | Error(HostNameMustContainDot invalidUri) -> Some invalidUri.value
        | Error(UriFormatException(invalidUri, _)) -> Some invalidUri.value)

let isText (s: string) = not (String.IsNullOrWhiteSpace s)
