module SimpleRssServer.RequestLog

open System
open System.IO
open System.Globalization

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
        |> Array.map (fun parts -> parts[1])
        |> Array.distinct
        |> Array.map Uri.create
        |> Array.choose (fun x ->
            match x with
            | Error e -> None
            | Ok u -> Some u)
        |> Array.filter (fun x -> x.Scheme = Uri.UriSchemeHttp || x.Scheme = Uri.UriSchemeHttps)
    else
        [||]
