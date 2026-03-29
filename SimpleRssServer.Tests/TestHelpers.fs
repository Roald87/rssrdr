module SimpleRssServer.Tests.TestHelpers

open System
open System.IO
open System.Globalization

open SimpleRssServer.DomainPrimitiveTypes

let deleteFile (filePath: OsPath) =
    if File.Exists filePath then
        File.Delete filePath

module DummyXmlFeedFactory =
    let articleTitle (i: int) = $"Article {i}"

    let private baseDate = DateTime(2024, 1, 1)

    let create (feedUrl: string) (count: int) : string =
        let items =
            [ 1..count ]
            |> List.map (fun i ->
                let date = baseDate.AddDays(float (i - 1))

                let pubDate =
                    date.ToString("ddd, dd MMM yyyy 00:00:00 +0000", CultureInfo.InvariantCulture)

                $"<item><title>{articleTitle i}</title><link>{feedUrl}/article/{i}</link><description>Description {i}</description><pubDate>{pubDate}</pubDate></item>")
            |> String.concat ""

        $"""<?xml version="1.0" encoding="UTF-8"?><rss version="2.0"><channel><title>Test Feed</title><link>{feedUrl}</link><description>Test Feed</description>{items}</channel></rss>"""
