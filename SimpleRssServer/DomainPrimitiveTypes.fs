module SimpleRssServer.DomainPrimitiveTypes

open System
open System.IO
open System.Collections.Specialized
open System.Web

type InvalidUri =
    | InvalidUri of string

    static member Create(s: string) = InvalidUri s

    member this.Value =
        let (InvalidUri s) = this
        s

type UriError =
    | HostNameMustContainDot of InvalidUri
    | UriFormatException of InvalidUri * Exception

type Uri with
    static member Create(s: string) =
        try
            let uri = Uri s

            if uri.Host.Contains "." then
                Ok uri
            else
                Error(HostNameMustContainDot(InvalidUri.Create s))
        with :? UriFormatException as ex ->
            Error(UriFormatException(InvalidUri.Create s, ex))

    static member BaseUrl(s: string) =
        try
            Uri(s).Host.Replace("www.", "")
        with _ ->
            ""

    static member StripScheme(s: string) =
        let s = s.Replace("http://", "")
        s.Replace("https://", "")

    static member CreateWithHttps(s: string) =
        let ensureScheme (s: string) =
            if
                s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            then
                s
            else
                $"https://{s}"

        Uri.Create(ensureScheme s)

type Filename = Filename of string

type OsPath =
    | OsPath of string

    static member (+)(OsPath a, b) = OsPath(a + b)

type Directory with
    static member CreateDirectory path =
        let (OsPath p) = path
        Directory.CreateDirectory p |> ignore

    static member DeleteRecursive(path: OsPath) =
        let (OsPath p) = path
        Directory.Delete(p, recursive = true)

    static member Exists path =
        let (OsPath p) = path
        Directory.Exists p

    static member GetFiles path =
        let (OsPath p) = path
        Directory.GetFiles p

type Path with
    static member Combine(path: OsPath, filename: Filename) =
        let (Filename f) = filename
        let (OsPath p) = path
        Path.Combine(p, f) |> OsPath

    static member Combine(path: OsPath, filename: string) =
        let (OsPath p) = path
        Path.Combine(p, filename) |> OsPath

    static member GetDirectoryName(path: OsPath) =
        let (OsPath p) = path
        Path.GetDirectoryName p |> OsPath

type File with
    static member Exists path =
        let (OsPath p) = path
        File.Exists p

    static member Delete path =
        let (OsPath p) = path
        File.Delete p

    static member GetLastWriteTime path =
        let (OsPath p) = path
        File.GetLastWriteTime p

    static member ReadAllLines path =
        let (OsPath p) = path
        File.ReadAllLines p

    static member ReadAllText path =
        let (OsPath p) = path
        File.ReadAllText p

    static member ReadAllTextAsync path =
        let (OsPath p) = path
        File.ReadAllTextAsync p

    static member SetLastWriteTime(path, lastWriteTime) =
        let (OsPath p) = path
        File.SetLastWriteTime(p, lastWriteTime)

    static member WriteAllLines(path, lines: string array) =
        let (OsPath p) = path
        File.WriteAllLines(p, lines)

    static member WriteAllLines(path, lines: string list) =
        let (OsPath p) = path
        File.WriteAllLines(p, lines)

    static member WriteAllText(path, content: string) =
        let (OsPath p) = path
        File.WriteAllText(p, content)

    static member WriteAllTextAsync(path, content: string) =
        let (OsPath p) = path
        File.WriteAllTextAsync(p, content)

type Html =
    | Html of string

    override this.ToString() = let (Html s) = this in s
    static member (+)(Html a, Html b) = Html(a + b)
    static member Empty = Html ""

type Query =
    | Query of NameValueCollection

    member this.Value =
        let (Query nvc) = this
        nvc

    static member Create(s: string) = Query(HttpUtility.ParseQueryString s)

    static member CreateWithKey(key: string, values: string array) : Query =
        let nvc = new NameValueCollection()
        values |> Array.map (fun value -> nvc.Add(key, value)) |> ignore
        Query nvc

    member this.GetValues(key: string) = this.Value.GetValues(key)

    override this.ToString() =
        let nvc = this.Value

        let pairs =
            nvc.AllKeys
            |> Array.collect (fun key -> nvc.GetValues key |> Array.map (fun v -> $"{key}={v}"))
            |> String.concat "&"

        if pairs.Length = 0 then "" else "?" + pairs
