module SimpleRssServer.Tests.RequestTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Globalization
open System.Threading.Tasks
open System.Text.Json
open Microsoft.Extensions.Logging.Abstractions

open Xunit

open SimpleRssServer.Config
open SimpleRssServer.Helper
open SimpleRssServer.Request
open SimpleRssServer.RssParser
open SimpleRssServer.RequestLog
open SimpleRssServer.HttpClient
open SimpleRssServer.HtmlRenderer
open SimpleRssServer.Cache
open SimpleRssServer.DomainPrimitiveTypes
open TestHelpers

let cacheConfig =
    let tempDir = Path.Combine(Path.GetTempPath(), "rssrdr_test_cache")
    Directory.CreateDirectory(tempDir) |> ignore

    { Dir = tempDir
      Expiration = TimeSpan.FromHours 1.0 }

let createOutdatedCache (cachePath: string) (content: string) =
    File.WriteAllText(cachePath, content)
    let cacheAge = DateTime.Now - 2.0 * cacheConfig.Expiration
    File.SetLastWriteTime(cachePath, cacheAge)

type MockHttpResponseHandler(response: HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request, cancellationToken) = Task.FromResult response

type MockHttpMessageHandler(sendAsyncImpl: HttpRequestMessage -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()
    override _.SendAsync(request, cancellationToken) = sendAsyncImpl request

let mockHttpClient (handler: HttpMessageHandler) = new HttpClient(handler)

let httpOkClient content =
    let responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
    responseMessage.Content <- new StringContent(content)

    let handler = new MockHttpResponseHandler(responseMessage)
    new HttpClient(handler)

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

let mockClientThrowsWhenCalled =
    let handler =
        new MockHttpMessageHandler(fun _ -> failwith "HTTP request not expected to be made")

    new HttpClient(handler)

[<Fact>]
let ``Test requestUrls returns two URLs from request-log.txt`` () =
    let logFilePath = "data/request-log.txt"

    let urls = readRequestLog logFilePath

    Assert.Equal(2, Array.length urls)
    Assert.Contains(Uri "https://example.com/feed1", urls)
    Assert.Contains(Uri "https://example.com/feed2", urls)

[<Fact>]
let ``Test updateRequestLog removes entries older than retention period`` () =
    let filename = "test_log_retention.txt"
    let retention = TimeSpan.FromDays 7.0

    let oldDate =
        DateTime.Now.AddDays(-8.0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let recentDate =
        DateTime.Now.AddDays(-3.0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let oldEntry = $"{oldDate} OldEntry"
    let recentEntry = $"{recentDate} RecentEntry"

    File.WriteAllLines(filename, [ oldEntry; recentEntry ])

    updateRequestLog filename retention [| Ok(Uri "http://newentry.nl") |]

    let fileContent = File.ReadAllLines filename

    Assert.DoesNotContain(oldEntry, fileContent)
    Assert.Contains(recentEntry, fileContent[0])
    Assert.Contains("http://newentry.nl", fileContent[1])

    deleteFile filename

[<Fact>]
let ``Test updateRequestLog creates file and appends strings with datetime`` () =
    let filename = "test_log.txt"

    let logEntries =
        [| Ok(Uri "https://Entry1.com")
           Ok(Uri "http://Entry2.ch")
           Ok(Uri "https://Entry3.nl") |]

    let retention = TimeSpan 1

    if File.Exists filename then
        File.Delete filename

    updateRequestLog filename retention logEntries
    Assert.True(File.Exists filename, "Expected log file to be created")

    let fileContent = File.ReadAllText filename

    let currentDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    logEntries
    |> validUris
    |> Array.iter (fun entry -> Assert.Contains($"{currentDate} {entry.AbsoluteUri}", fileContent))

    deleteFile filename

[<Fact>]
let ``Test getRssUrls`` () =
    let result = getRssUrls "?rss=https://abs.com/test"

    Assert.Equal<Result<Uri, UriError>[]>([| Ok(Uri "https://abs.com/test") |], result)

[<Fact>]
let ``Test getRssUrls with two URLs`` () =
    let result = getRssUrls "?rss=https://abs.com/test1&rss=https://abs.com/test2"

    let expected =
        [| Ok(Uri "https://abs.com/test1"); Ok(Uri "https://abs.com/test2") |]

    Assert.Equal<Result<Uri, UriError>[]>(expected, result)

[<Fact>]
let ``Test getRssUrls with empty string`` () =
    let result = getRssUrls ""

    Assert.Equal<Result<Uri, UriError>[]>([||], result)

[<Fact>]
let ``Test getRssUrls with invalid URL`` () =
    let result = getRssUrls "?rss=invalid-url"
    Assert.Equal(1, result.Length)

    match result.[0] with
    | Error(HostNameMustContainDot msg) -> Assert.Contains("invalid-url", InvalidUri.value msg)
    | _ -> Assert.True(false, "Expected Error HostNameMustContainDot")

[<Fact>]
let ``Test getRssUrls with valid and invalid URLs`` () =
    let result = getRssUrls "?rss=invalid-url&rss=https://valid-url.com"
    Assert.Equal(2, result.Length)

    match result.[0] with
    | Error(HostNameMustContainDot msg) -> Assert.Contains("invalid-url", InvalidUri.value msg)
    | _ -> Assert.True(false, "Expected Error HostNameMustContainDot")

    match result.[1] with
    | Ok uri -> Assert.Equal(Uri "https://valid-url.com", uri)
    | Error _ -> Assert.True(false, "Expected Ok, got Error")

[<Fact>]
let ``Test getRssUrls adds https if missing`` () =
    let result = getRssUrls "?rss=example.com/feed&rss=http://example.com/feed2"

    let expected =
        [| Ok(Uri "https://example.com/feed"); Ok(Uri "http://example.com/feed2") |]

    Assert.Equal<Result<Uri, UriError>[]>(expected, result)

[<Fact>]
let ``Test convertUrlToFilename`` () =
    Assert.Equal(Filename "https_abc_com_test", convertUrlToValidFilename (Uri "https://abc.com/test"))

    Assert.Equal(
        Filename "https_abc_com_test_rss_blabla",
        convertUrlToValidFilename (Uri "https://abc.com/test?rss=blabla")
    )

[<Fact>]
let ``Test getAsync with successful response`` () =
    let expectedContent = "Hello, world!"
    let client = httpOkClient expectedContent
    let logger = NullLogger.Instance

    let result =
        fetchUrlAsync client logger (Uri "http://example.com") (Some DateTimeOffset.Now) (TimeSpan.FromSeconds 5.0)
        |> Async.RunSynchronously

    match result with
    | Ok result -> Assert.Equal(expectedContent, result)
    | Error error -> Assert.True(false, error)

[<Fact>]
let ``Test getAsync with unsuccessful response on real page`` () =
    let client = new HttpClient()
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
    | Ok _ -> Assert.False(true, "Expected Failure but got Success")
    | Error errorMsg -> Assert.Contains("Exception", errorMsg)

[<Fact>]
let ``GetAsync returns NotModified or OK based on IfModifiedSince header`` () =
    // Arrange
    let url = Uri "http://example.com"
    let lastModifiedDate = DateTimeOffset(DateTime(2023, 1, 1))
    let client = mockHttpClient (createDynamicResponse lastModifiedDate)
    let logger = NullLogger.Instance

    // Case 1: When If-Modified-Since is equal to lastModifiedDate
    let result1 =
        fetchUrlAsync client logger url (Some lastModifiedDate) (TimeSpan.FromSeconds 5.0)
        |> Async.RunSynchronously

    match result1 with
    | Ok content -> Assert.Equal("No changes", content)
    | Error error -> failwithf "Expected success, but got failure: %s" error

    // Case 2: When If-Modified-Since is before lastModifiedDate
    let earlierDate = lastModifiedDate.AddDays -1.0

    let result2 =
        fetchUrlAsync client logger url (Some earlierDate) (TimeSpan.FromSeconds 5.0)
        |> Async.RunSynchronously

    match result2 with
    | Ok content -> Assert.Equal("Content has changed since the last modification date", content)
    | Error error -> failwithf "Expected success, but got failure: %s" error

    // Case 3: When If-Modified-Since is not provided
    let result3 =
        fetchUrlAsync client logger url None (TimeSpan.FromSeconds 5.0)
        |> Async.RunSynchronously

    match result3 with
    | Ok content -> Assert.Equal("Content has changed since the last modification date", content)
    | Error error -> failwithf "Expected success, but got failure: %s" error

[<Fact>]
let ``Test fetchWithCache with no cache`` () =
    let url = Uri "http://example.com/test"
    let expectedContent = "Mock response content"
    let client = httpOkClient expectedContent

    let filename = convertUrlToValidFilename url

    let filePath = Path.Combine(cacheConfig.Dir, filename)

    // Ensure the file does not exist before the test
    deleteFile filePath

    let result = fetchUrlWithCacheAsync client cacheConfig url |> Async.RunSynchronously

    match result with
    | Ok _ ->
        Assert.True(File.Exists filePath, "Expected file to be created")
        let fileContent = File.ReadAllText filePath
        Assert.Equal(expectedContent, fileContent)
    | Error error -> Assert.True(false, error)

    deleteFile filePath

[<Fact>]
let ``Test fetchWithCache with non expired cache`` () =
    let url = Uri "http://example.com/testabc123"
    let expectedContent = "Cached response content"

    // Create a mock handler that throws an exception if called
    let handler =
        new MockHttpMessageHandler(fun _ -> failwith "HTTP request should not be made")

    let client = new HttpClient(handler)

    let filename = convertUrlToValidFilename url

    let filePath = Path.Combine(cacheConfig.Dir, filename)

    // Write the expected content to the file and set its last write time to less than 1 hour ago
    File.WriteAllText(filePath, expectedContent)
    let cacheAge = DateTime.Now - cacheConfig.Expiration * 0.5
    File.SetLastWriteTime(filePath, cacheAge)

    let result = fetchUrlWithCacheAsync client cacheConfig url |> Async.RunSynchronously

    match result with
    | Ok content -> Assert.Equal(expectedContent, content)
    | Error msg -> Assert.True(false, $"Expected success but got error: {msg}")

    deleteFile filePath

[<Fact>]
let ``Test fetchWithCache with expired cache`` () =
    let url = Uri "http://example.com/testxyz789"
    let cachedContent = "Old cached response content"
    let newContent = "New response content"
    let client = httpOkClient newContent

    let filename = convertUrlToValidFilename url

    let filePath = Path.Combine(cacheConfig.Dir, filename)

    createOutdatedCache filePath cachedContent

    let result = fetchUrlWithCacheAsync client cacheConfig url |> Async.RunSynchronously

    match result with
    | Ok content ->
        Assert.Equal(newContent, content)
        let fileContent = File.ReadAllText filePath
        Assert.Equal(newContent, fileContent)
    | Error error -> Assert.True(false, error)

    deleteFile filePath

[<Fact>]
let ``Test fetchWithCache with expired cache and 304 response`` () =
    let url = Uri "http://example.com/testasdf456"
    let cachedContent = "Old cached response content"
    let responseMessage = new HttpResponseMessage(HttpStatusCode.NotModified)

    let handler = new MockHttpResponseHandler(responseMessage)
    let client = new HttpClient(handler)

    let filename = convertUrlToValidFilename url

    let filePath = Path.Combine(cacheConfig.Dir, filename)

    // Write the cached content to the file and set its last write time to more than 1 hour ago
    File.WriteAllText(filePath, cachedContent)
    let oldWriteTime = DateTime.Now - 2.0 * cacheConfig.Expiration
    File.SetLastWriteTime(filePath, oldWriteTime)

    let result = fetchUrlWithCacheAsync client cacheConfig url |> Async.RunSynchronously

    match result with
    | Ok content ->
        Assert.Equal(cachedContent, content)
        let newWriteTime = File.GetLastWriteTime filePath
        Assert.True(newWriteTime > oldWriteTime, "Expected file write time to be updated")
    | Error error -> Assert.True(false, error)

    deleteFile filePath

[<Fact>]
let ``Test fetchWithCache with expired cache and 304 NotModified should clear failure record`` () =
    let url = Uri "http://example.com/test-304"
    let cachedContent = "Old cached content"

    let filename = convertUrlToValidFilename url

    let filePath = Path.Combine(cacheConfig.Dir, filename)
    let failurePath = filePath + ".failures"

    createOutdatedCache filePath cachedContent

    // Create a failure record indicating previous failures
    let failure =
        { LastFailure = DateTimeOffset.Now.AddHours -3.0
          ConsecutiveFailures = 2 }

    let json = System.Text.Json.JsonSerializer.Serialize failure
    File.WriteAllText(failurePath, json)

    // Handler returns 304 Not Modified
    let responseMessage = new HttpResponseMessage(HttpStatusCode.NotModified)
    let handler = new MockHttpResponseHandler(responseMessage)
    let client = new HttpClient(handler)

    let result = fetchUrlWithCacheAsync client cacheConfig url |> Async.RunSynchronously

    match result with
    | Ok content ->
        Assert.Equal(cachedContent, content)
        Assert.False(File.Exists failurePath, "Expected failure record to be removed after successful fetch")
    | Error error -> Assert.True(false, error)

    deleteFile filePath
    deleteFile failurePath

[<Fact>]
let ``Test fetchWithCache respects failure backoff when retry is not allowed and cache is expired`` () =
    let url = Uri "http://example.com/test-backoff"
    let cachedContent = "Cached response content"

    let filename = convertUrlToValidFilename url

    let filePath = Path.Combine(cacheConfig.Dir, filename)
    let failurePath = filePath + ".failures"

    createOutdatedCache filePath cachedContent

    // Create a failure record indicating 2 failures (should wait 2 hours)
    let failure =
        { LastFailure = DateTimeOffset.Now.AddMinutes -30.0 // Only 30 minutes ago
          ConsecutiveFailures = 2 }

    let json = System.Text.Json.JsonSerializer.Serialize failure
    File.WriteAllText(failurePath, json)

    // Act
    let result =
        fetchUrlWithCacheAsync mockClientThrowsWhenCalled cacheConfig url
        |> Async.RunSynchronously

    // Assert
    match result with
    | Ok content -> Assert.False(true, "Should return an error due to backoff period")
    | Error error -> Assert.Equal(error, $"Previous request(s) to {url} failed. You can retry in 1.5 hours.")

    // Cleanup
    deleteFile filePath
    deleteFile failurePath

[<Fact>]
let ``Test fetchWithCache attempts retry when backoff period has passed and cache is expired`` () =
    let url = Uri "http://example.com/test-retry"
    let cachedContent = "Old cached content"
    let newContent = "New content after retry"

    let client = httpOkClient newContent

    let filename = convertUrlToValidFilename url

    let filePath = Path.Combine(cacheConfig.Dir, filename)
    let failurePath = filePath + ".failures"

    createOutdatedCache filePath cachedContent

    // Create a failure record that's old enough to allow retry
    let failure =
        { LastFailure = DateTimeOffset.Now.AddHours -3.0 // 3 hours ago
          ConsecutiveFailures = 2 // Would normally require 2 hour wait
        }

    let json = System.Text.Json.JsonSerializer.Serialize(failure)
    File.WriteAllText(failurePath, json)

    // Act
    let result = fetchUrlWithCacheAsync client cacheConfig url |> Async.RunSynchronously

    // Assert - should have attempted HTTP request and got new content
    match result with
    | Ok content ->
        Assert.Equal(newContent, content)
        // Failure record should be deleted after successful fetch
        Assert.False(File.Exists failurePath, "Expected failure record to be cleared after successful fetch")
    | Error error -> Assert.True(false, $"Expected success with new content but got error: {error}")

    // Cleanup
    deleteFile filePath
    deleteFile failurePath

[<Fact>]
let ``Test fetchWithCache returns error with expired cache and cooldown time when retrying too soon`` () =
    let url = Uri "http://example.com/test-cooldown"
    let cachedContent = "Cached response content"

    let filename = convertUrlToValidFilename url

    let filePath = Path.Combine(cacheConfig.Dir, filename)
    let failurePath = failureFilePath filePath

    createOutdatedCache filePath cachedContent

    // Create failure record with 2 consecutive failures (should wait 2 hours)
    let failure =
        { LastFailure = DateTimeOffset.Now.AddMinutes -30.0 // Only 30 minutes ago
          ConsecutiveFailures = 2 }

    let json = JsonSerializer.Serialize(failure)
    File.WriteAllText(failurePath, json)

    // Act
    let result =
        fetchUrlWithCacheAsync mockClientThrowsWhenCalled cacheConfig url
        |> Async.RunSynchronously

    // Assert
    match result with
    | Error error -> Assert.Contains($"Previous request(s) to {url} failed. You can retry in 1.5 hours.", error)
    | Ok _ -> Assert.True(false, "Expected error message with cooldown time but got success")

    // Cleanup
    deleteFile filePath
    deleteFile failurePath

[<Fact>]
let ``Test Html encoding of special characters`` () =
    let expected =
        """
    <div>
        <h2><a href="https://rachelbythebay.com/w/2024/02/24/signext/" target="_blank">1 &lt;&lt; n vs. 1U &lt;&lt; n and a cell phone autofocus problem</a></h2>
        <div class="source-date">rachelbythebay.com on Sunday, February 25, 2024</div>
        <p>Maybe 15 years ago, I heard that a certain cell phone camera would lose the ability to autofocus for about two weeks, then it would go back to working for another two weeks, and so on. It had something to do with the time ( since the epoch), the bits in u...</p>
    </div>
    """

    let actual =
        { Title = "1 << n vs. 1U << n and a cell phone autofocus problem"
          Text =
            "Maybe 15 years ago, I heard that a certain cell phone camera would lose the ability to autofocus for about two weeks, then it would go back to working for another two weeks, and so on. It had something to do with the time ( since the epoch), the bits in u..."
          PostDate = Some(DateTime(2024, 02, 25))
          Url = "https://rachelbythebay.com/w/2024/02/24/signext/"
          BaseUrl = "rachelbythebay.com" }
        |> convertArticleToHtml
        |> string

    Assert.Equal(expected, actual)

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
    | Ok _ -> Assert.True(false, "Expected timeout failure but got success")
    | Error error -> Assert.Contains("timed out after 1 seconds", error)

[<Fact>]
let ``Test requestUrls skips invalid URLs in log file`` () =
    let filename = "test_invalid_urls.txt"

    let lines =
        [| "2025-06-23 https://valid-url.com/feed1"
           "2025-06-23 not-a-valid-url"
           "2025-06-23 https://valid-url.com/feed2"
           "2025-06-23 "
           " sd sdfa weq"
           "  a     "
           "\t \t"
           "2025-06-23 ftp://unsupported-protocol.com/feed3"
           "2025-06-23 https://valid-url.com/feed1" |]

    File.WriteAllLines(filename, lines)

    let urls =
        try
            readRequestLog filename
        with _ ->
            [||]

    Assert.Contains(Uri "https://valid-url.com/feed1", urls)
    Assert.Contains(Uri "https://valid-url.com/feed2", urls)
    Assert.DoesNotContain(Uri "ftp://unsupported-protocol.com/feed3", urls)
    Assert.Equal(2, Array.length urls)
    File.Delete(filename)
