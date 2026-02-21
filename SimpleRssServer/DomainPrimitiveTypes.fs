module SimpleRssServer.DomainPrimitiveTypes

open System
open System.IO

type Filename = Filename of string

module InvalidUri =
    type _T = InvalidUri of string

    let create (s: string) = InvalidUri s

    let value (InvalidUri u) = u

type UriError =
    | HostNameMustContainDot of InvalidUri._T
    | UriFormatException of InvalidUri._T * Exception

type Uri with
    static member create(s: string) =
        try
            let uri = Uri s

            if uri.Host.Contains "." then
                Ok uri
            else
                Error(HostNameMustContainDot(InvalidUri.create s))
        with :? UriFormatException as ex ->
            Error(UriFormatException(InvalidUri.create s, ex))

    static member createWithHttps(s: string) =
        let ensureScheme (s: string) =
            if
                s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            then
                s
            else
                $"https://{s}"

        Uri.create (ensureScheme s)


type Path with
    static member Combine(path1: string, filename: Filename) =
        let (Filename s) = filename
        Path.Combine(path1, s)
