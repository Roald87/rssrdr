module SimpleRssServer.RequestLog

open System
open System.Globalization

open SimpleRssServer.Config
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes

let private dateFormat = "yyyy-MM-dd"

let private expectedColumns = [ "url"; "date" ].Length

let private readLogParts (logPath: OsPath) : string[][] =
    if OsFile.exists logPath then
        OsFile.readAllLines logPath
        |> Array.map (fun line -> line.Trim().Split(' ', expectedColumns))
        |> Array.filter (fun parts -> parts.Length = expectedColumns)
    else
        [||]

let private readLogMap (logPath: OsPath) : Map<string, DateTime> =
    readLogParts logPath
    |> Array.choose (fun parts ->
        match DateTime.TryParseExact(parts[0], dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, date -> Some(parts[1], date)
        | _ -> None)
    |> Map.ofArray

let private writeLogMap (logPath: OsPath) (retention: TimeSpan) (logMap: Map<string, DateTime>) =
    let cutoff = (DateTime.Now - retention).Date

    logMap
    |> Map.toList
    |> List.filter (fun (_, date) -> date.Date >= cutoff)
    |> List.map (fun (url, date) -> $"{date.ToString(dateFormat, CultureInfo.InvariantCulture)} {url}")
    |> OsFile.writeAllLines logPath

let updateRequestLog (requestLogPath: OsPath) (retention: TimeSpan) (uris: Uri list) =
    let today = DateTime.Now
    let existing = readLogMap requestLogPath

    let updated =
        uris |> List.fold (fun m (uri: Uri) -> Map.add uri.AbsoluteUri today m) existing

    writeLogMap requestLogPath retention updated

let uniqueValidRequestLogUrls (logPath: OsPath) =
    readLogParts logPath
    |> Array.map (fun parts -> parts[1])
    |> Array.distinct
    |> Array.toList
    |> List.map FeedUri.create
    |> List.choose Result.toOption
    |> List.filter (fun x -> x.Scheme = Uri.UriSchemeHttp || x.Scheme = Uri.UriSchemeHttps)

let logSuccessfulFeedRequestsAndParses (logPath: OsPath) (upss: UriProcessState list) =
    upss
    |> List.choose (function
        | ParsedLiveFeed(_, feed) -> Some(Uri feed.Link)
        | ParsedCachedFeed feed -> Some(Uri feed.Link)
        | _ -> None)
    |> updateRequestLog logPath RequestLogRetention

    upss
