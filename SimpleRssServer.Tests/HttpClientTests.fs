module SimpleRssServer.Tests.HttpClientTests

open Microsoft.Extensions.Logging.Abstractions
open System
open System.Net
open System.Net.Http
open System.Threading.Tasks

open Xunit

open SimpleRssServer.HttpClient
open SimpleRssServer.Helper

type MockHttpResponseHandler(response: HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request, cancellationToken) = Task.FromResult(response)

[<Fact>]
let ``Test fetchUrlAsync with successful response`` () =
    let expectedContent = "Hello, world!"
    let responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
    responseMessage.Content <- new StringContent(expectedContent)

    let handler = new MockHttpResponseHandler(responseMessage)
    let client = new HttpClient(handler)
    let logger = NullLogger.Instance

    let result =
        fetchUrlAsync client logger (Uri "http://example.com") (Some DateTimeOffset.Now) 5.0
        |> Async.RunSynchronously

    match result with
    | Ok result -> Assert.Equal(expectedContent, result)
    | Error error -> Assert.True(false, error)

[<Fact>]
let ``Test fetchUrlAsync with unsuccessful response`` () =
    let client = new HttpClient()
    let logger = NullLogger.Instance

    let response =
        fetchUrlAsync client logger (Uri "https://thisurldoesntexistforsureordoesit.com") (Some DateTimeOffset.Now) 5.0
        |> Async.RunSynchronously

    match response with
    | Ok _ -> Assert.False(true, "Expected Failure but got Success")
    | Error errorMsg -> Assert.Contains("Exception", errorMsg)
