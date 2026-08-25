module SimpleRssServer.Helper

open System
open DomainPrimitiveTypes
open SimpleRssServer.DomainModel

// https://stackoverflow.com/a/3722671/6329629
let (|Prefix|_|) (p: string) (s: string) =
    if s.StartsWith p then Some(s.Substring p.Length) else None

let (|Suffix|_|) (suf: string) (s: string) =
    if s.EndsWith suf then
        Some(s.Substring(0, s.Length - suf.Length))
    else
        None

let (|StripQuery|) (s: string) = s.Split('?').[0]

/// Matches a "/s/<id>/shuffle" path (with an optional query string), capturing the collection id.
let (|CollectionShuffleId|_|) (s: string) =
    match s with
    | Prefix "/s/" (StripQuery(Suffix "/shuffle" collectionId)) -> Some(CollectionId collectionId)
    | _ -> None

/// Matches a "/s/<id>" path (with an optional query string), capturing the collection id.
let (|CollectionIdPath|_|) (s: string) =
    match s with
    | Prefix "/s/" (StripQuery collectionId) -> Some(CollectionId collectionId)
    | _ -> None

let validUris (uris: Result<Uri, UriError> list) : Uri list = uris |> List.choose Result.toOption

let isText (s: string) = not (String.IsNullOrWhiteSpace s)

let toUriProcessState (uri: Result<Uri, UriError>) : UriProcessState =
    match uri with
    | Ok u -> TryFetchFromCache u
    | Error u ->
        match u with
        | HostNameMustContainDot iu -> ProcessingError(InvalidUriHostname iu)
        | UriFormatException(iu, ex) -> ProcessingError(InvalidUriFormat(iu, ex))
