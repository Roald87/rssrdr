module SimpleRssServer.RequestLog

open System
open System.IO
open System.Globalization

let updateRequestLog (filename: string) (retention: TimeSpan) (uris: Uri list) =
    let currentDate = DateTime.Now

    let currentDateString =
        currentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let logEntries =
        uris |> List.map (fun url -> $"{currentDateString} {url.AbsoluteUri}")

    let existingEntries =
        if File.Exists filename then
            File.ReadAllLines filename
            |> Array.toList
            |> List.filter (fun line ->
                let datePart = line.Split(' ').[0]

                let entryDate =
                    DateTime.ParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture)

                currentDate - entryDate <= retention)
        else
            []

    let updatedEntries = List.append existingEntries logEntries
    File.WriteAllLines(filename, updatedEntries)

let requestUrls logPath =
    if File.Exists logPath then
        File.ReadAllLines logPath
        |> Array.map (fun line -> line.Split(' ').[1])
        |> Array.distinct
        |> Array.toList
        |> List.map Uri
    else
        []
