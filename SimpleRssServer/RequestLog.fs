module SimpleRssServer.RequestLog

open System
open System.IO
open System.Globalization

open SimpleRssServer.Config
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes

let updateRequestLog (requestLogPath: OsPath) (retention: TimeSpan) (uris: Uri list) =
    let currentDate = DateTime.Now
    let dateFormat = "yyyy-MM-dd"

    let currentDateString =
        currentDate.ToString(dateFormat, CultureInfo.InvariantCulture)

    let newEntries =
        uris |> List.map (fun url -> $"{currentDateString} {url.AbsoluteUri}")

    let existingEntries =
        if OsFile.exists requestLogPath then
            OsFile.readAllLines requestLogPath
            |> Array.toList
            |> List.filter (fun line ->
                let datePart = line.Split(' ', 2)[0]

                let entryDate =
                    DateTime.ParseExact(datePart, dateFormat, CultureInfo.InvariantCulture)

                currentDate - entryDate <= retention)
        else
            []

    OsFile.writeAllLines requestLogPath (existingEntries @ newEntries)

let uniqueValidRequestLogUrls (logPath: OsPath) =
    if OsFile.exists logPath then
        let expectedColumns = 2

        OsFile.readAllLines logPath
        |> Array.toList
        |> List.map (fun line -> line.Trim().Split(' ', expectedColumns))
        |> List.filter (fun parts -> parts.Length = expectedColumns)
        |> List.map (fun parts -> parts[1])
        |> List.distinct
        |> List.map FeedUri.create
        |> List.choose Result.toOption
        |> List.filter (fun x -> x.Scheme = Uri.UriSchemeHttp || x.Scheme = Uri.UriSchemeHttps)
    else
        []

let logSuccessfulFeedRequestsAndParses (logPath: OsPath) (upss: UriProcessState list) =
    upss
    |> List.choose (function
        | ParsedFeed(_, feed) -> Some(Uri feed.Link)
        | ParsedCachedFeed feed -> Some(Uri feed.Link)
        | _ -> None)
    |> updateRequestLog logPath RequestLogRetention

    upss
