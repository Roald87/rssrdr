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

module FeedUri =
    let create (s: string) =
        try
            let uri = Uri s

            if uri.Host.Contains "." then
                Ok uri
            else
                Error(HostNameMustContainDot(InvalidUri.Create s))
        with :? UriFormatException as ex ->
            Error(UriFormatException(InvalidUri.Create s, ex))

    let baseUrl (s: string) =
        try
            Uri(s).Host.Replace("www.", "")
        with _ ->
            ""

    let removeScheme (s: string) =
        let s = s.Replace("http://", "")
        s.Replace("https://", "")

    let removeHttpsScheme (s: string) =
        if s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then
            s.Substring 8
        else
            s

    let createWithHttps (s: string) =
        let ensureScheme (s: string) =
            if
                s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            then
                s
            else
                $"https://{s}"

        create (ensureScheme s)

type Filename = Filename of string

type OsPath =
    | OsPath of string

    static member (+)(OsPath a, b) = OsPath(a + b)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module OsPath =
    let combine (OsPath path) (Filename filename) = Path.Combine(path, filename) |> OsPath
    let join (OsPath path) (filename: string) = Path.Combine(path, filename) |> OsPath
    let getDirectoryName (OsPath path) = Path.GetDirectoryName path |> OsPath

module OsDirectory =
    let create (OsPath path) =
        Directory.CreateDirectory path |> ignore

    let deleteRecursive (OsPath path) =
        Directory.Delete(path, recursive = true)

    let exists (OsPath path) = Directory.Exists path
    let getFiles (OsPath path) = Directory.GetFiles path

module OsFile =
    let exists (OsPath path) = File.Exists path
    let delete (OsPath path) = File.Delete path
    let getLastWriteTime (OsPath path) = File.GetLastWriteTime path
    let readAllLines (OsPath path) = File.ReadAllLines path
    let readAllText (OsPath path) = File.ReadAllText path

    let setLastWriteTime (OsPath path) lastWriteTime =
        File.SetLastWriteTime(path, lastWriteTime)

    let writeAllLines (OsPath path) (lines: string list) = File.WriteAllLines(path, lines)
    let writeAllText (OsPath path) (content: string) = File.WriteAllText(path, content)

type Html =
    | Html of string

    override this.ToString() = let (Html s) = this in s
    static member (+)(Html a, Html b) = Html(a + b)
    static member Empty = Html ""

    static member Concat(htmls: Html seq) =
        htmls |> Seq.map string |> String.concat "" |> Html

type Query =
    | Query of NameValueCollection

    member this.Value =
        let (Query nvc) = this
        nvc

    static member Create(s: string) = Query(HttpUtility.ParseQueryString s)

    static member CreateWithKey(key: string, values: string list) : Query =
        let nvc = NameValueCollection()
        values |> List.iter (fun value -> nvc.Add(key, value))
        Query nvc

    member this.GetValues(key: string) =
        this.Value.GetValues key
        |> Option.ofObj
        |> Option.defaultValue [||]
        |> Array.toList

    override this.ToString() =
        let nvc = this.Value

        let pairs =
            nvc.AllKeys
            |> Array.collect (fun key -> nvc.GetValues key |> Array.map (fun v -> $"{key}={v}"))
            |> String.concat "&"

        if pairs.Length = 0 then "" else "?" + pairs
