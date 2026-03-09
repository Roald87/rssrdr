module SimpleRssServer.Tests.ProgramTests

#nowarn "25" // Incomplete pattern matches are intentional in tests asserting specific DU cases

open Microsoft.Extensions.Logging.Abstractions
open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Xunit

open SimpleRssServer.DomainPrimitiveTypes
open Program
open RequestTests

let minimalRss =
    """<?xml version="1.0" encoding="UTF-8"?><rss version="2.0"><channel><title>Test</title><link>https://example.com</link><description>Test</description></channel></rss>"""

let httpClientWithResponses (responses: Map<string, HttpResponseMessage>) =
    let handler =
        new MockHttpMessageHandler(fun request ->
            let url = request.RequestUri.AbsoluteUri

            match Map.tryFind url responses with
            | Some response -> Task.FromResult response
            | None -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))

    new HttpClient(handler)

// Generate unique URIs for test, because it is not possible to reuse a URI
// and give it a different response.
let guids (count: int) =
    [| for i in 1..count -> Guid.NewGuid().ToString() |]

[<Fact>]
let ``Test assembleRssFeeds with empty rssUrls results in empty query`` () =
    // Arrange
    let client = httpOkClient ""

    let rssUrls = [||]

    // Act
    let (FeedsReady(_, page)) =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls
        |> Async.RunSynchronously

    let result = page |> string

    // Assert
    Assert.Contains("<a href=\"config.html/\">config/</a>", result)

[<Fact>]
let ``Test assembleRssFeeds includes config link with query and removes https prefix`` () =
    // Arrange
    let client = httpOkClient ""

    let ids = guids 3

    let urls =
        [| $"https://example.com/feed{ids[0]}"
           $"https://example.com/feed{ids[1]}"
           $"http://example.com/feed{ids[2]}" |]

    let rssUrls = urls |> Array.map Uri.Create

    // Act
    let (FeedsReady(_, page)) =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls
        |> Async.RunSynchronously

    let result = page |> string

    let expectedQuery =
        $"?rss=example.com/feed{ids[0]}&rss=example.com/feed{ids[1]}&rss=http://example.com/feed{ids[2]}"

    Assert.Contains($"<a href=\"config.html/%s{expectedQuery}\">config/</a>", result)

[<Fact>]
let ``Test assembleRssFeeds returns successful URIs for happy path with two valid URIs`` () =
    // Arrange
    let client = httpOkClient minimalRss

    let urls = guids 2 |> Array.map (fun id -> Uri $"https://example.com/feed{id}")
    let rssUrls = urls |> Array.map Ok

    // Act
    let (FeedsReady(successfulUris, _)) =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls
        |> Async.RunSynchronously

    // Assert
    Assert.Equal(2, successfulUris.Length)
    Assert.Contains(urls[0], successfulUris)
    Assert.Contains(urls[1], successfulUris)

[<Fact>]
let ``Test assembleRssFeeds returns only successful URIs for mix of invalid and failed fetches`` () =
    // Arrange
    let okResponse = new HttpResponseMessage(HttpStatusCode.OK)
    okResponse.Content <- new StringContent(minimalRss)

    let errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError)

    let urls = guids 3 |> Array.map (fun id -> $"https://example.com/feed{id}")

    let responses =
        Map.ofList [ urls[0], okResponse; urls[1], errorResponse; urls[2], okResponse ]

    let client = httpClientWithResponses responses

    let rssUrls =
        [| Uri.Create "invalid"
           Uri.Create urls[0] // valid and success
           Uri.Create urls[1] // valid but fetch fails
           Uri.Create urls[2] |] // valid and success

    // Act
    let (FeedsReady(successfulUris, _)) =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls
        |> Async.RunSynchronously

    // Assert
    Assert.Equal(2, successfulUris.Length)
    Assert.Contains(Uri urls[0], successfulUris)
    Assert.Contains(Uri urls[2], successfulUris)

[<Fact>]
let ``Test assembleRssFeeds excludes URI that returns HTML from successful URIs`` () =
    // Arrange
    let htmlContent =
        "<html><head><title>Not RSS</title></head><body>Test</body></html>"

    let ids = guids 2

    let rssUrl = Uri $"https://example.com/feed{ids[0]}"
    let htmlUrl = Uri $"https://example.com/feed{ids[1]}"

    let responses =
        Map.ofList
            [ rssUrl.AbsoluteUri,
              (let r = new HttpResponseMessage(HttpStatusCode.OK) in
               r.Content <- new StringContent(minimalRss)
               r)
              htmlUrl.AbsoluteUri,
              (let r = new HttpResponseMessage(HttpStatusCode.OK) in
               r.Content <- new StringContent(htmlContent)
               r) ]

    let client = httpClientWithResponses responses
    let rssUrls = [| Ok rssUrl; Ok htmlUrl |]

    // Act
    let (FeedsReady(successfulUris, _)) =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls
        |> Async.RunSynchronously

    // Assert
    Assert.Equal(1, successfulUris.Length)
    Assert.Contains(rssUrl, successfulUris)
    Assert.DoesNotContain(htmlUrl, successfulUris)

[<Fact>]
let ``Test assembleRssFeeds with HTML page containing single discovered feed returns FeedsReady`` () =
    // Arrange
    let ids = guids 2
    let htmlUrl = Uri $"https://example.com/page{ids[0]}"
    let feedUrl = $"https://example.com/feed{ids[1]}"

    let htmlContent =
        $"""<html><head><link rel="alternate" type="application/rss+xml" title="Feed" href="{feedUrl}"></head><body></body></html>"""

    let htmlResponse = new HttpResponseMessage(HttpStatusCode.OK)
    htmlResponse.Content <- new StringContent(htmlContent)

    let feedResponse = new HttpResponseMessage(HttpStatusCode.OK)
    feedResponse.Content <- new StringContent(minimalRss)

    let responses =
        Map.ofList [ htmlUrl.AbsoluteUri, htmlResponse; feedUrl, feedResponse ]

    let client = httpClientWithResponses responses
    let rssUrls = [| Ok htmlUrl |]

    // Act
    let result =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls
        |> Async.RunSynchronously

    // Assert
    match result with
    | FeedsReady(uris, _) ->
        Assert.Equal(1, uris.Length)
        Assert.Contains(Uri feedUrl, uris)
    | NeedsSelection _ -> failwith "Expected FeedsReady but got NeedsSelection"

[<Fact>]
let ``Test assembleRssFeeds with HTML page containing two discovered feeds returns NeedsSelection`` () =
    // Arrange
    let id = Guid.NewGuid().ToString()
    let htmlUrl = Uri $"https://example.com/page{id}"

    let htmlContent =
        """<html><head>
        <link rel="alternate" type="application/rss+xml" title="RSS Feed" href="https://example.com/rss.xml">
        <link rel="alternate" type="application/atom+xml" title="Atom Feed" href="https://example.com/atom.xml">
        </head><body></body></html>"""

    let htmlResponse = new HttpResponseMessage(HttpStatusCode.OK)
    htmlResponse.Content <- new StringContent(htmlContent)

    let responses = Map.ofList [ htmlUrl.AbsoluteUri, htmlResponse ]
    let client = httpClientWithResponses responses
    let rssUrls = [| Ok htmlUrl |]

    // Act
    let result =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls
        |> Async.RunSynchronously

    // Assert
    match result with
    | NeedsSelection(_, toSelect) -> Assert.Equal(2, toSelect.Length)
    | FeedsReady _ -> failwith "two-feed HTML: Expected NeedsSelection but got FeedsReady"

[<Fact>]
let ``Test assembleRssFeeds with mix of valid RSS and HTML with two feeds returns NeedsSelection`` () =
    // Arrange
    let ids = guids 2
    let rssUrl = Uri $"https://example.com/feed{ids[0]}"
    let htmlUrl = Uri $"https://example.com/page{ids[1]}"

    let htmlContent =
        """<html><head>
        <link rel="alternate" type="application/rss+xml" title="RSS Feed" href="https://example.com/rss.xml">
        <link rel="alternate" type="application/atom+xml" title="Atom Feed" href="https://example.com/atom.xml">
        </head><body></body></html>"""

    let rssResponse = new HttpResponseMessage(HttpStatusCode.OK)
    rssResponse.Content <- new StringContent(minimalRss)

    let htmlResponse = new HttpResponseMessage(HttpStatusCode.OK)
    htmlResponse.Content <- new StringContent(htmlContent)

    let responses =
        Map.ofList [ rssUrl.AbsoluteUri, rssResponse; htmlUrl.AbsoluteUri, htmlResponse ]

    let client = httpClientWithResponses responses
    let rssUrls = [| Ok rssUrl; Ok htmlUrl |]

    // Act
    let result =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls
        |> Async.RunSynchronously

    // Assert
    match result with
    | NeedsSelection(confirmedRss, toSelect) ->
        Assert.Equal(1, confirmedRss.Length)
        Assert.Contains(rssUrl, confirmedRss)
        Assert.Equal(2, toSelect.Length)
    | FeedsReady _ -> failwith "Expected NeedsSelection but got FeedsReady"
