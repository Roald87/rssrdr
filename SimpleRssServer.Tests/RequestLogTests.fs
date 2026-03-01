module SimpleRssServer.Tests.RequestLogTests

open System
open System.IO
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

    let oldEntry = $"{oldDate} OldEntry"
    let recentEntry = $"{recentDate} RecentEntry"

    File.WriteAllLines(filename, [ oldEntry; recentEntry ])

    updateRequestLog filename retention [| Uri "http://newentry.com" |]

    let fileContent = File.ReadAllLines filename

    Assert.DoesNotContain(oldEntry, fileContent)
    Assert.Contains(recentEntry, fileContent[0])
    Assert.Contains("newentry.com", fileContent[1])

    deleteFile filename
