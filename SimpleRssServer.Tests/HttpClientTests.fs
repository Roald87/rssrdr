module SimpleRssServer.Tests.HttpClientTests

open Microsoft.Extensions.Logging.Abstractions
open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Xunit

open SimpleRssServer.DomainModel
open SimpleRssServer.HttpClient
open TestHelpers

type MockHttpResponseHandler(response: HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(_, _) = Task.FromResult response

type FailingHttpMessageHandler() =
    inherit HttpMessageHandler()

    override _.SendAsync(_, _) =
        Task.FromException<HttpResponseMessage>(HttpRequestException("Simulated network failure"))

[<Fact>]
let ``Test fetchUrlAsync with successful response`` () =
    let expectedContent = "Hello, world!"
    let responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
    responseMessage.Content <- new StringContent(expectedContent)

    let handler = new MockHttpResponseHandler(responseMessage)
    let client = new HttpClient(handler)
    let logger = NullLogger.Instance

    let result =
        fetchUrlAsync client logger (Uri "http://example.com") (Some DateTimeOffset.Now) (TimeSpan.FromSeconds 5.0)
        |> Async.RunSynchronously

    Assert.Equal(expectedContent, getOk result)

[<Fact>]
let ``Test fetchUrlAsync with unsuccessful response`` () =
    let client = new HttpClient(new FailingHttpMessageHandler())
    let logger = NullLogger.Instance

    let response =
        fetchUrlAsync
            client
            logger
            (Uri "https://thisurldoesntexistforsureordoesit.com")
            (Some DateTimeOffset.Now)
            (TimeSpan.FromSeconds 5.0)
        |> Async.RunSynchronously

    match response with
    | Error(HttpException _) -> Assert.True(true, "timed out")
    | Error error -> failwithf $"Got unexpected error: {error}"
    | Ok x -> failwithf $"Expected Error but got OK {x}"

let createDynamicResponse (lastModifiedDate: DateTimeOffset) =
    new MockHttpMessageHandler(fun request ->
        let ifModifiedSince = request.Headers.IfModifiedSince

        if ifModifiedSince.HasValue && ifModifiedSince.Value >= lastModifiedDate then
            new HttpResponseMessage(HttpStatusCode.NotModified) |> Task.FromResult
        else
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent "Content has changed since the last modification date"
            response.Content.Headers.LastModified <- Nullable lastModifiedDate
            response |> Task.FromResult)

[<Fact>]
let ``GetAsync returns NotModified or OK based on IfModifiedSince header`` () =
    // Arrange
    let url = Uri "http://example.com"
    let lastModifiedDate = DateTimeOffset(DateTime(2023, 1, 1))
    let client = new HttpClient(createDynamicResponse lastModifiedDate)
    let logger = NullLogger.Instance

    // Case 1: When If-Modified-Since is equal to lastModifiedDate
    let result1 =
        fetchUrlAsync client logger url (Some lastModifiedDate) (TimeSpan.FromSeconds 5.0)
        |> Async.RunSynchronously

    Assert.Equal("No changes", getOk result1)

    // Case 2: When If-Modified-Since is before lastModifiedDate
    let earlierDate = lastModifiedDate.AddDays -1.0

    let result2 =
        fetchUrlAsync client logger url (Some earlierDate) (TimeSpan.FromSeconds 5.0)
        |> Async.RunSynchronously

    Assert.Equal("Content has changed since the last modification date", getOk result2)

    // Case 3: When If-Modified-Since is not provided
    let result3 =
        fetchUrlAsync client logger url None (TimeSpan.FromSeconds 5.0)
        |> Async.RunSynchronously

    Assert.Equal("Content has changed since the last modification date", getOk result3)

type DelayedResponseHandler(delay: TimeSpan) =
    inherit HttpMessageHandler()

    override _.SendAsync(request, cancellationToken) =
        async {
            do! Task.Delay(delay, cancellationToken) |> Async.AwaitTask

            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent "Delayed response"
            return response
        }
        |> Async.StartAsTask

[<Fact>]
let ``GetAsync returns timeout error when request takes too long`` () =
    let timeout = TimeSpan.FromSeconds 1.0
    let delay = TimeSpan.FromSeconds(timeout.TotalSeconds + 0.2) // Longer than the timeout
    let handler = new DelayedResponseHandler(delay)
    let client = new HttpClient(handler)
    let logger = NullLogger.Instance

    let result =
        fetchUrlAsync client logger (Uri "http://example.com") None timeout
        |> Async.RunSynchronously

    match result with
    | Error(HttpRequestTimedOut _) -> Assert.True(true, "Got expected timeout error")
    | Error error -> failwithf $"Got unexpected error: {error}"
    | Ok x -> failwithf $"Expected timeout failure but got success {x}"
