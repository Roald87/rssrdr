module SimpleRssServer.Helper

open System
open DomainPrimitiveTypes
open SimpleRssServer.DomainModel

// https://stackoverflow.com/a/3722671/6329629
let (|Prefix|_|) (p: string) (s: string) =
    if s.StartsWith p then Some(s.Substring p.Length) else None

let validUris (uris: Result<Uri, UriError> array) : Uri array = uris |> Array.choose Result.toOption

let isText (s: string) = not (String.IsNullOrWhiteSpace s)

let toUriProcessState (uri: Result<Uri, UriError>) : UriProcessState =
    match uri with
    | Ok u -> ValidUri(Some DateTimeOffset.Now, u)
    | Error u ->
        match u with
        | HostNameMustContainDot iu -> ProcessingError(InvalidUriHostname iu)
        | UriFormatException(iu, ex) -> ProcessingError(InvalidUriFormat(iu, ex))
