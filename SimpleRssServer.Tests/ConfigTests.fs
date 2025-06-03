module SimpleRssServer.Tests.ConfigTests

open Xunit
open SimpleRssServer.Config

[<Fact>]
let ``Test Config values are correct`` () =
    Assert.Equal("rss-cache", CachePath)
    Assert.Equal(5.0, RequestTimeout)
    Assert.Equal("rss-cache/request-log.txt", RequestLogPath)
    Assert.Equal(System.TimeSpan.FromDays 7, RequestLogRetention)
