module SimpleRssServer.Tests.RequestTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Globalization
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions

open Xunit

open SimpleRssServer.Helper
open SimpleRssServer.Request
open SimpleRssServer.RssParser
open SimpleRssServer.RequestLog
open SimpleRssServer.HttpClient
open SimpleRssServer.HtmlRenderer
open TestHelpers

[<Fact>]
let ``Test assembleRssFeeds with empty rssUrls results in empty query`` () =
    // Arrange
    let client = new HttpClient()
    let cacheLocation = "test_cache"
    let rssUrls = [||]

    // Act
    let result =
        assembleRssFeeds NullLogger.Instance Chronological client cacheLocation rssUrls
        |> string

    // Assert
    Assert.Contains($"<a href=\"config.html/\">config/</a>", result)

[<Fact>]
let ``Test assembleRssFeeds includes config link with query and removes https prefix`` () =
    // Arrange
    let client = new HttpClient()
    let cacheLocation = "test_cache"

    let rssUrls =
        [| Ok(Uri "https://example.com/feed")
           Ok(Uri "https://example.com/feed2")
           Ok(Uri "http://example.com/feed3") |]

    // Act
    let result =
        assembleRssFeeds NullLogger.Instance Chronological client cacheLocation rssUrls
        |> string

    let expectedQuery =
        $"?rss=example.com/feed&rss=example.com/feed2&rss=http://example.com/feed3"

    Assert.Contains($"<a href=\"config.html/%s{expectedQuery}\">config/</a>", result)

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

    Assert.Equal<Result<Uri, string>[]>([| Ok(Uri "https://abs.com/test") |], result)

[<Fact>]
let ``Test getRssUrls with two URLs`` () =
    let result = getRssUrls "?rss=https://abs.com/test1&rss=https://abs.com/test2"

    let expected: Result<Uri, string>[] =
        [| Ok(Uri "https://abs.com/test1"); Ok(Uri "https://abs.com/test2") |]

    Assert.Equal<Result<Uri, string>[]>(expected, result)

[<Fact>]
let ``Test getRssUrls with empty string`` () =
    let result = getRssUrls ""

    Assert.Equal<Result<Uri, string>[]>([||], result)

[<Fact>]
let ``Test getRssUrls with invalid URL`` () =
    let result = getRssUrls "?rss=invalid-url"
    Assert.Equal(1, result.Length)

    match result.[0] with
    | Error msg -> Assert.Contains("invalid-url", msg)
    | Ok _ -> Assert.True(false, "Expected Error, got Ok")

[<Fact>]
let ``Test getRssUrls with valid and invalid URLs`` () =
    let result = getRssUrls "?rss=invalid-url&rss=https://valid-url.com"
    Assert.Equal(2, result.Length)

    match result.[0] with
    | Error msg -> Assert.Contains("invalid-url", msg)
    | Ok _ -> Assert.True(false, "Expected Error, got Ok")

    match result.[1] with
    | Ok uri -> Assert.Equal(Uri "https://valid-url.com", uri)
    | Error _ -> Assert.True(false, "Expected Ok, got Error")

[<Fact>]
let ``Test getRssUrls adds https if missing`` () =
    let result = getRssUrls "?rss=example.com/feed&rss=http://example.com/feed2"

    let expected =
        [| Ok(Uri "https://example.com/feed"); Ok(Uri "http://example.com/feed2") |]

    Assert.Equal<Result<Uri, string>[]>(expected, result)

[<Fact>]
let ``Test convertUrlToFilename`` () =
    Assert.Equal("https_abc_com_test", convertUrlToValidFilename (Uri "https://abc.com/test"))
    Assert.Equal("https_abc_com_test_rss_blabla", convertUrlToValidFilename (Uri "https://abc.com/test?rss=blabla"))

type MockHttpResponseHandler(response: HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request, cancellationToken) = Task.FromResult response

[<Fact>]
let ``Test getAsync with successful response`` () =
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
let ``Test getAsync with unsuccessful response on real page`` () =
    let client = new HttpClient()
    let logger = NullLogger.Instance

    let response =
        fetchUrlAsync client logger (Uri "https://thisurldoesntexistforsureordoesit.com") (Some DateTimeOffset.Now) 5.0
        |> Async.RunSynchronously

    match response with
    | Ok _ -> Assert.False(true, "Expected Failure but got Success")
    | Error errorMsg -> Assert.Contains("Exception", errorMsg)

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
            response.Content <- new StringContent "Content has changed since the last modification date"
            response.Content.Headers.LastModified <- Nullable lastModifiedDate
            response |> Task.FromResult)

[<Fact>]
let ``GetAsync returns NotModified or OK based on IfModifiedSince header`` () =
    // Arrange
    let url = Uri "http://example.com"
    let lastModifiedDate = DateTimeOffset(DateTime(2023, 1, 1))
    let client = mockHttpClient (createDynamicResponse lastModifiedDate)
    let logger = NullLogger.Instance

    // Case 1: When If-Modified-Since is equal to lastModifiedDate
    let result1 =
        fetchUrlAsync client logger url (Some lastModifiedDate) 5
        |> Async.RunSynchronously

    match result1 with
    | Ok content -> Assert.Equal("No changes", content)
    | Error error -> failwithf "Expected success, but got failure: %s" error

    // Case 2: When If-Modified-Since is before lastModifiedDate
    let earlierDate = lastModifiedDate.AddDays -1.0

    let result2 =
        fetchUrlAsync client logger url (Some earlierDate) 5 |> Async.RunSynchronously

    match result2 with
    | Ok content -> Assert.Equal("Content has changed since the last modification date", content)
    | Error error -> failwithf "Expected success, but got failure: %s" error

    // Case 3: When If-Modified-Since is not provided
    let result3 = fetchUrlAsync client logger url None 5 |> Async.RunSynchronously

    match result3 with
    | Ok content -> Assert.Equal("Content has changed since the last modification date", content)
    | Error error -> failwithf "Expected success, but got failure: %s" error

[<Fact>]
let ``Test fetchWithCache with no cache`` () =
    let url = Uri "http://example.com/test"
    let expectedContent = "Mock response content"
    let responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
    responseMessage.Content <- new StringContent(expectedContent)

    let handler = new MockHttpResponseHandler(responseMessage)
    let client = new HttpClient(handler)

    let filename = convertUrlToValidFilename url
    let currentDir = Directory.GetCurrentDirectory()
    let filePath = Path.Combine(currentDir, filename)

    // Ensure the file does not exist before the test
    deleteFile filePath

    let result = fetchUrlWithCacheAsync client currentDir url |> Async.RunSynchronously

    match result with
    | Ok _ ->
        Assert.True(File.Exists filePath, "Expected file to be created")
        let fileContent = File.ReadAllText filePath
        Assert.Equal(expectedContent, fileContent)
    | Error error -> Assert.True(false, error)

    deleteFile filePath

[<Fact>]
let ``Test fetchWithCache with existing cache less than 1 hour old`` () =
    let url = Uri "http://example.com/testabc123"
    let expectedContent = "Cached response content"

    // Create a mock handler that throws an exception if called
    let handler =
        new MockHttpMessageHandler(fun _ -> failwith "HTTP request should not be made")

    let client = new HttpClient(handler)

    let filename = convertUrlToValidFilename url
    let currentDir = Directory.GetCurrentDirectory()
    let filePath = Path.Combine(currentDir, filename)

    // Write the expected content to the file and set its last write time to less than 1 hour ago
    File.WriteAllText(filePath, expectedContent)
    File.SetLastWriteTime(filePath, DateTime.Now.AddMinutes -30.0)

    let result = fetchUrlWithCacheAsync client currentDir url |> Async.RunSynchronously

    match result with
    | Ok content -> Assert.Equal(expectedContent, content)
    | Error _ -> Assert.True(false, "Expected success but got error.")

    deleteFile filePath

[<Fact>]
let ``Test fetchWithCache with existing cache more than 1 hour old`` () =
    let url = Uri "http://example.com/testxyz789"
    let cachedContent = "Old cached response content"
    let newContent = "New response content"
    let responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
    responseMessage.Content <- new StringContent(newContent)

    let handler = new MockHttpResponseHandler(responseMessage)
    let client = new HttpClient(handler)

    let filename = convertUrlToValidFilename url
    let currentDir = Directory.GetCurrentDirectory()
    let filePath = Path.Combine(currentDir, filename)

    // Write the cached content to the file and set its last write time to more than 1 hour ago
    File.WriteAllText(filePath, cachedContent)
    File.SetLastWriteTime(filePath, DateTime.Now.AddHours -2.0)

    let result = fetchUrlWithCacheAsync client currentDir url |> Async.RunSynchronously

    match result with
    | Ok content ->
        Assert.Equal(newContent, content)
        let fileContent = File.ReadAllText filePath
        Assert.Equal(newContent, fileContent)
    | Error error -> Assert.True(false, error)

    deleteFile filePath

[<Fact>]
let ``Test fetchWithCache with existing cache more than 1 hour old and 304 response`` () =
    let url = Uri "http://example.com/testasdf456"
    let cachedContent = "Old cached response content"
    let responseMessage = new HttpResponseMessage(HttpStatusCode.NotModified)

    let handler = new MockHttpResponseHandler(responseMessage)
    let client = new HttpClient(handler)

    let filename = convertUrlToValidFilename url
    let currentDir = Directory.GetCurrentDirectory()
    let filePath = Path.Combine(currentDir, filename)

    // Write the cached content to the file and set its last write time to more than 1 hour ago
    File.WriteAllText(filePath, cachedContent)
    let oldWriteTime = DateTime.Now.AddHours -2.0
    File.SetLastWriteTime(filePath, oldWriteTime)

    let result = fetchUrlWithCacheAsync client currentDir url |> Async.RunSynchronously

    match result with
    | Ok content ->
        Assert.Equal(cachedContent, content)
        let newWriteTime = File.GetLastWriteTime filePath
        Assert.True(newWriteTime > oldWriteTime, "Expected file write time to be updated")
    | Error error -> Assert.True(false, error)

    deleteFile filePath

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

type DelayedResponseHandler(delay: TimeSpan) =
    inherit HttpMessageHandler()

    override _.SendAsync(request, cancellationToken) =
        async {
            do! Async.Sleep(int delay.TotalMilliseconds)
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent("Delayed response")
            return response
        }
        |> Async.StartAsTask

[<Fact>]
let ``GetAsync returns timeout error when request takes too long`` () =
    let timeout = 1.0
    let delay = TimeSpan.FromSeconds(timeout + 0.2) // Longer than the 5 second timeout
    let handler = new DelayedResponseHandler(delay)
    let client = new HttpClient(handler)
    let logger = NullLogger.Instance

    let result =
        fetchUrlAsync client logger (Uri "http://example.com") None timeout
        |> Async.RunSynchronously

    match result with
    | Ok _ -> Assert.True(false, "Expected timeout failure but got success")
    | Error error -> Assert.Contains($"timed out after {timeout} seconds", error)

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
