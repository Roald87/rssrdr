module SimpleRssServer.RequestLog

open System
open System.IO
open System.Globalization

open SimpleRssServer.Helper
open SimpleRssServer.DomainPrimitiveTypes

let updateRequestLog (requestLogPath: OsPath) (retention: TimeSpan) (uris: Uri array) =
    let currentDate = DateTime.Now

    let currentDateString =
        currentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let newEntries =
        uris |> Array.map (fun url -> $"{currentDateString} {url.AbsoluteUri}")

    let existingEntries =
        if File.Exists requestLogPath then
            File.ReadAllLines requestLogPath
            |> Array.filter (fun line ->
                let datePart = line.Split(' ', 2)[0]

                let entryDate =
                    DateTime.ParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture)

                currentDate - entryDate <= retention)
        else
            [||]

    let updatedEntries = Array.append existingEntries newEntries
    File.WriteAllLines(requestLogPath, updatedEntries)

let readRequestLog (logPath: OsPath) =
    if File.Exists logPath then
        let expectedColumns = 2

        File.ReadAllLines logPath
        |> Array.map (fun line -> line.Trim().Split(' ', expectedColumns))
        |> Array.filter (fun parts -> parts.Length = expectedColumns)
        |> Array.choose (fun parts ->
            try
                let uri = Uri parts[1]

                if uri.Scheme = "http" || uri.Scheme = "https" then
                    Some uri
                else
                    None
            with :? UriFormatException ->
                None)
        |> Array.distinct
    else
        [||]
