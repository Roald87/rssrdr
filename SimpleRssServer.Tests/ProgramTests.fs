module SimpleRssServer.Tests.ProgramTests

open System
open Xunit
open RequestTests
open Program
open Microsoft.Extensions.Logging.Abstractions

[<Fact>]
let ``Test assembleRssFeeds with empty rssUrls results in empty query`` () =
    // Arrange
    let client = httpOkClient ""

    let rssUrls = [||]

    // Act
    let result =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls
        |> string

    // Assert
    Assert.Contains($"<a href=\"config.html/\">config/</a>", result)

[<Fact>]
let ``Test assembleRssFeeds includes config link with query and removes https prefix`` () =
    // Arrange
    let client = httpOkClient ""

    let rssUrls =
        [| Ok(Uri "https://example.com/feed")
           Ok(Uri "https://example.com/feed2")
           Ok(Uri "http://example.com/feed3") |]

    // Act
    let result =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls
        |> string

    let expectedQuery =
        $"?rss=example.com/feed&rss=example.com/feed2&rss=http://example.com/feed3"

    Assert.Contains($"<a href=\"config.html/%s{expectedQuery}\">config/</a>", result)
