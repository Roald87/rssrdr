module SimpleRssServer.Tests.RequestTests

open Xunit
open SimpleRssServer.Helper
open SimpleRssServer.Request
open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open System.Net

[<Fact>]
let ``Test getRequestInfo`` () =
    let result = getRssUrls "?rss=https://abs.com/test"

    Assert.Equal(Some [ "https://abs.com/test" ], result)

[<Fact>]
let ``Test getRequestInfo with two URLs`` () =
    let result = getRssUrls "?rss=https://abs.com/test1&rss=https://abs.com/test2"

    Assert.Equal(Some [ "https://abs.com/test1"; "https://abs.com/test2" ], result)

[<Fact>]
let ``Test getRequestInfo with empty string`` () =
    let result = getRssUrls ""

    Assert.Equal(None, result)

[<Fact>]
let ``Test convertUrlToFilename`` () =
    Assert.Equal("https_abc_com_test", convertUrlToValidFilename "https://abc.com/test")
    Assert.Equal("https_abc_com_test_rss_blabla", convertUrlToValidFilename "https://abc.com/test?rss=blabla")

type MockHttpResponseHandler(response: HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request, cancellationToken) = Task.FromResult(response)

[<Fact>]
let ``Test getAsync with successful response`` () =
    let expectedContent = "Hello, world!"
    let responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
    responseMessage.Content <- new StringContent(expectedContent)

    let handler = new MockHttpResponseHandler(responseMessage)
    let client = new HttpClient(handler)

    let result =
        getAsync client "http://example.com" (Some DateTimeOffset.Now)
        |> Async.RunSynchronously

    match result with
    | Success result -> Assert.Equal(expectedContent, result)
    | Failure error -> Assert.True(false, error)

[<Fact>]
let ``Test getAsync with unsuccessful response on real page`` () =
    let client = new HttpClient()

    let response =
        getAsync client "https://thisurldoesntexistforsureordoesit.com" (Some DateTimeOffset.Now)
        |> Async.RunSynchronously

    match response with
    | Success _ -> Assert.False(true, "Expected Failure but got Success")
    | Failure errorMsg -> Assert.Contains("Exception", errorMsg)

type MockHttpMessageHandler(sendAsyncImpl: HttpRequestMessage -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()
    override _.SendAsync(request, cancellationToken) = sendAsyncImpl request

let mockHttpClient (handler: HttpMessageHandler) = new HttpClient(handler)

let createDynamicResponse (lastModifiedDate: DateTimeOffset) =
    new MockHttpMessageHandler(fun request ->
        let ifModifiedSince = request.Headers.IfModifiedSince

        if ifModifiedSince.HasValue && ifModifiedSince.Value >= lastModifiedDate then
            new HttpResponseMessage(HttpStatusCode.NotModified) |> Task.FromResult
        else
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent("Content has changed since the last modification date")
            response.Content.Headers.LastModified <- Nullable(lastModifiedDate)
            response |> Task.FromResult)

[<Fact>]
let ``GetAsync returns NotModified or OK based on IfModifiedSince header`` () =
    // Arrange
    let url = "http://example.com"
    let lastModifiedDate = DateTimeOffset(DateTime(2023, 1, 1))
    let client = mockHttpClient (createDynamicResponse lastModifiedDate)

    // Case 1: When If-Modified-Since is equal to lastModifiedDate
    let result1 = getAsync client url (Some lastModifiedDate) |> Async.RunSynchronously

    match result1 with
    | Success content -> Assert.Equal("No changes", content)
    | Failure error -> failwithf "Expected success, but got failure: %s" error

    // Case 2: When If-Modified-Since is before lastModifiedDate
    let earlierDate = lastModifiedDate.AddDays(-1.0)
    let result2 = getAsync client url (Some earlierDate) |> Async.RunSynchronously

    match result2 with
    | Success content -> Assert.Equal("Content has changed since the last modification date", content)
    | Failure error -> failwithf "Expected success, but got failure: %s" error

    // Case 3: When If-Modified-Since is not provided
    let result3 = getAsync client url None |> Async.RunSynchronously

    match result3 with
    | Success content -> Assert.Equal("Content has changed since the last modification date", content)
    | Failure error -> failwithf "Expected success, but got failure: %s" error
