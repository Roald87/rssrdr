module SimpleRssServer.RequestLog

open System
open System.IO
open System.Globalization

open SimpleRssServer.Helper

let updateRequestLog (filename: string) (retention: TimeSpan) (uris: Result<Uri, string> array) =
    let currentDate = DateTime.Now

    let currentDateString =
        currentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let logEntries =
        uris
        |> validUris
        |> Array.map (fun url -> $"{currentDateString} {url.AbsoluteUri}")

    let existingEntries =
        if File.Exists filename then
            File.ReadAllLines filename
            |> Array.filter (fun line ->
                let datePart = line.Split(' ').[0]

                let entryDate =
                    DateTime.ParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture)

                currentDate - entryDate <= retention)
        else
            [||]

    let updatedEntries = Array.append existingEntries logEntries
    File.WriteAllLines(filename, updatedEntries)

let requestUrls logPath =
    if File.Exists logPath then
        File.ReadAllLines logPath
        |> Array.map (fun line -> line.Trim().Split([| ' ' |], 2))
        |> Array.filter (fun parts -> parts.Length = 2 && isText parts.[1])
        |> Array.map (fun parts -> parts.[1].Trim())
        |> Array.choose (fun url ->
            try
                let uri = Uri url

                if uri.Scheme = "http" || uri.Scheme = "https" then
                    Some uri
                else
                    None
            with :? UriFormatException ->
                None)
        |> Array.distinct
    else
        [||]
