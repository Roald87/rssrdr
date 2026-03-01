module SimpleRssServer.Tests.ProgramTests

open Microsoft.Extensions.Logging.Abstractions
open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Xunit

open SimpleRssServer.DomainPrimitiveTypes
open Program
open RequestTests

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
    let _, page =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls

    let result = page |> string

    // Assert
    Assert.Contains($"<a href=\"config.html/\">config/</a>", result)

[<Fact>]
let ``Test assembleRssFeeds includes config link with query and removes https prefix`` () =
    // Arrange
    let client = httpOkClient ""

    let ids = guids 3

    let urls =
        [| $"https://example.com/feed{ids[0]}"
           $"https://example.com/feed{ids[1]}"
           $"http://example.com/feed{ids[2]}" |]

    let rssUrls = urls |> Array.map Uri.create

    // Act
    let _, page =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls

    let result = page |> string

    let expectedQuery =
        $"?rss=example.com/feed{ids[0]}&rss=example.com/feed{ids[1]}&rss=http://example.com/feed{ids[2]}"

    Assert.Contains($"<a href=\"config.html/%s{expectedQuery}\">config/</a>", result)

[<Fact>]
let ``Test assembleRssFeeds returns successful URIs for happy path with two valid URIs`` () =
    // Arrange
    let client = httpOkClient ""

    let urls = guids 2 |> Array.map (fun id -> Uri $"https://example.com/feed{id}")
    let rssUrls = urls |> Array.map (fun url -> Ok url)

    // Act
    let successfulUris, _ =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls

    // Assert
    Assert.Equal(2, successfulUris.Length)
    Assert.Contains(urls[0], successfulUris)
    Assert.Contains(urls[1], successfulUris)

[<Fact>]
let ``Test assembleRssFeeds returns only successful URIs for mix of invalid and failed fetches`` () =
    // Arrange
    let okResponse = new HttpResponseMessage(HttpStatusCode.OK)
    okResponse.Content <- new StringContent ""

    let errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError)

    let urls = guids 3 |> Array.map (fun id -> $"https://example.com/feed{id}")

    let responses =
        Map.ofList [ urls[0], okResponse; urls[1], errorResponse; urls[2], okResponse ]

    let client = httpClientWithResponses responses

    let rssUrls =
        [| Uri.create "invalid"
           Uri.create urls[0] // valid and success
           Uri.create urls[1] // valid but fetch fails
           Uri.create urls[2] |] // valid and success

    // Act
    let successfulUris, _ =
        assembleRssFeeds NullLogger.Instance Chronological client cacheConfig rssUrls

    // Assert
    Assert.Equal(2, successfulUris.Length)
    Assert.Contains(Uri urls[0], successfulUris)
    Assert.Contains(Uri urls[2], successfulUris)
