module SimpleRssServer.Tests.RequestLogTests

open System
open System.Globalization
open Xunit

open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.RequestLog
open TestHelpers

[<Fact>]
let ``Test updateRequestLog removes old entries`` () =
    let filename = OsPath "test_log_retention.txt"
    let retention = TimeSpan.FromDays 7.0

    let oldDate =
        DateTime.Now.AddDays(-8.0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let recentDate =
        DateTime.Now.AddDays(-3.0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    OsFile.writeAllLines filename [ $"{oldDate} http://oldentry.com"; $"{recentDate} http://recententry.com" ]

    updateRequestLog filename retention [ Uri "http://newentry.com" ]

    let fileContent = OsFile.readAllLines filename

    Assert.DoesNotContain("oldentry.com", fileContent)
    Assert.Contains(fileContent, fun line -> line.Contains "recententry.com")
    Assert.Contains(fileContent, fun line -> line.Contains "newentry.com")

    deleteFile filename

[<Fact>]
let ``Test updateRequestLog creates file and appends strings with datetime`` () =
    let filename = OsPath "test_log.txt"

    let logEntries =
        [ Uri "https://Entry1.com"; Uri "http://Entry2.ch"; Uri "https://Entry3.nl" ]

    let retention = TimeSpan 1

    if OsFile.exists filename then
        OsFile.delete filename

    updateRequestLog filename retention logEntries
    Assert.True(OsFile.exists filename, "Expected log file to be created")

    let fileContent = OsFile.readAllText filename

    let currentDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    logEntries
    |> List.iter (fun entry -> Assert.Contains($"{currentDate} {entry.AbsoluteUri}", fileContent))

    deleteFile filename

[<Fact>]
let ``Test updateRequestLog does not duplicate URLs`` () =
    let filename = OsPath "test_log_dedup.txt"
    let retention = TimeSpan.FromDays 7.0

    if OsFile.exists filename then
        OsFile.delete filename

    let url = Uri "https://example.com/feed"
    updateRequestLog filename retention [ url ]
    updateRequestLog filename retention [ url ]

    let fileContent = OsFile.readAllLines filename

    Assert.Equal(1, fileContent.Length)
    Assert.Contains("example.com/feed", fileContent[0])

    deleteFile filename

[<Fact>]
let ``Test requestUrls returns two URLs from request-log.txt`` () =
    let logFilePath = OsPath "data/request-log.txt"

    let urls = uniqueValidRequestLogUrls logFilePath

    Assert.Equal(2, urls.Length)
    Assert.Contains(Uri "https://example.com/feed1", urls)
    Assert.Contains(Uri "https://example.com/feed2", urls)

[<Fact>]
let ``Test requestUrls skips invalid URLs in log file`` () =
    let filename = OsPath "test_invalid_urls.txt"

    let lines =
        [ "2025-06-23 https://valid-url.com/feed1"
          "2025-06-23 not-a-valid-url"
          "2025-06-23 https://valid-url.com/feed2"
          "2025-06-23 "
          " sd sdfa weq"
          "  a     "
          "\t \t"
          "2025-06-23 ftp://unsupported-protocol.com/feed3"
          "2025-06-23 https://valid-url.com/feed1" ]

    OsFile.writeAllLines filename lines

    let urls =
        try
            uniqueValidRequestLogUrls filename
        with _ ->
            []

    Assert.Contains(Uri "https://valid-url.com/feed1", urls)
    Assert.Contains(Uri "https://valid-url.com/feed2", urls)
    Assert.DoesNotContain(Uri "ftp://unsupported-protocol.com/feed3", urls)
    Assert.Equal(2, urls.Length)
    OsFile.delete filename
