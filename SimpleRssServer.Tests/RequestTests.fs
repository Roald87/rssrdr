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
open SimpleRssServer.DomainModel
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
    Directory.CreateDirectory tempDir |> ignore

    { Dir = OsPath tempDir
      Expiration = TimeSpan.FromHours 1.0 }

let createOutdatedCache (cachePath: OsPath) (content: string) =
    File.WriteAllText(cachePath, content)
    let cacheAge = DateTime.Now - 2.0 * cacheConfig.Expiration
    File.SetLastWriteTime(cachePath, cacheAge)

type MockHttpResponseHandler(response: HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request, cancellationToken) = Task.FromResult response

type MockHttpMessageHandler(sendAsyncImpl: HttpRequestMessage -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()
    let mutable callCount = 0
    member _.CallCount = callCount

    override _.SendAsync(request, cancellationToken) =
        Threading.Interlocked.Increment(&callCount) |> ignore
        sendAsyncImpl request

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
    let logFilePath = OsPath "data/request-log.txt"

    let urls = uniqueValidRequestLogUrls logFilePath

    Assert.Equal(2, Array.length urls)
    Assert.Contains(Uri "https://example.com/feed1", urls)
    Assert.Contains(Uri "https://example.com/feed2", urls)

[<Fact>]
let ``Test updateRequestLog removes entries older than retention period`` () =
    let filename = OsPath "test_log_retention.txt"
    let retention = TimeSpan.FromDays 7.0

    let oldDate =
        DateTime.Now.AddDays(-8.0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let recentDate =
        DateTime.Now.AddDays(-3.0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let oldEntry = $"{oldDate} OldEntry"
    let recentEntry = $"{recentDate} RecentEntry"

    File.WriteAllLines(filename, [ oldEntry; recentEntry ])

    updateRequestLog filename retention [| Uri "http://newentry.nl" |]

    let fileContent = File.ReadAllLines filename

    Assert.DoesNotContain(oldEntry, fileContent)
    Assert.Contains(recentEntry, fileContent[0])
    Assert.Contains("http://newentry.nl", fileContent[1])

    deleteFile filename

[<Fact>]
let ``Test updateRequestLog creates file and appends strings with datetime`` () =
    let filename = OsPath "test_log.txt"

    let logEntries =
        [| Uri "https://Entry1.com"; Uri "http://Entry2.ch"; Uri "https://Entry3.nl" |]

    let retention = TimeSpan 1

    if File.Exists filename then
        File.Delete filename

    updateRequestLog filename retention logEntries
    Assert.True(File.Exists filename, "Expected log file to be created")

    let fileContent = File.ReadAllText filename

    let currentDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    logEntries
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
    | Error(HostNameMustContainDot url) -> Assert.Contains("invalid-url", url.Value)
    | x -> failwithf $"Expected Error HostNameMustContainDot, but got {x}"

[<Fact>]
let ``Test getRssUrls with valid and invalid URLs`` () =
    let result = getRssUrls "?rss=invalid-url&rss=https://valid-url.com"
    Assert.Equal(2, result.Length)

    match result.[0] with
    | Error(HostNameMustContainDot url) -> Assert.Contains("invalid-url", url.Value)
    | x -> failwithf $"Expected Error HostNameMustContainDot, but got {x}"

    match result.[1] with
    | Ok uri -> Assert.Equal(Uri "https://valid-url.com", uri)
    | Error error -> failwithf $"Expected Ok, got Error: {error}"

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
    | Error error -> failwithf $"Expected OK but got Error: {error}"

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

    match result1 with
    | Ok content -> Assert.Equal("No changes", content)
    | Error error -> failwithf $"Expected success, but got failure: {error}"

    // Case 2: When If-Modified-Since is before lastModifiedDate
    let earlierDate = lastModifiedDate.AddDays -1.0

    let result2 =
        fetchUrlAsync client logger url (Some earlierDate) (TimeSpan.FromSeconds 5.0)
        |> Async.RunSynchronously

    match result2 with
    | Ok content -> Assert.Equal("Content has changed since the last modification date", content)
    | Error error -> failwithf $"Expected success, but got failure: {error}"

    // Case 3: When If-Modified-Since is not provided
    let result3 =
        fetchUrlAsync client logger url None (TimeSpan.FromSeconds 5.0)
        |> Async.RunSynchronously

    match result3 with
    | Ok content -> Assert.Equal("Content has changed since the last modification date", content)
    | Error error -> failwithf $"Expected success, but got failure: {error}"

[<Fact>]
let ``Test Html encoding of special characters`` () =
    let actual =
        { Title = "1 << n vs. 1U << n and a cell phone autofocus problem"
          Text =
            "Maybe 15 years ago, I heard that a certain cell phone camera would lose the ability to autofocus for about two weeks, then it would go back to working for another two weeks, and so on. It had something to do with the time ( since the epoch), the bits in u..."
          PostDate = Some(DateTime(2024, 02, 25))
          ArticleUrl = "https://rachelbythebay.com/w/2024/02/24/signext/"
          FeedUrl = "https://rachelbythebay.com/feed" }
        |> convertArticleToHtml Html.Empty
        |> string

    Assert.Contains(
        """<a href="https://rachelbythebay.com/w/2024/02/24/signext/" target="_blank">1 &lt;&lt; n vs. 1U &lt;&lt; n and a cell phone autofocus problem</a>""",
        actual
    )

    Assert.Contains("""<div class="source-date">rachelbythebay.com on Sunday, February 25, 2024""", actual)
    Assert.Contains("Maybe 15 years ago, I heard that a certain cell phone camera", actual)

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

// [<Fact>]
// let ``cacheSuccessfulFetch creates cache file with correct content`` () =
//     let url = Uri "http://example.com/cache-write-test"
//     let content = "Fresh RSS content"
//     let filePath = Path.Combine(cacheConfig.Dir, convertUrlToValidFilename url)
//     deleteFile filePath

//     cacheSuccessfulFetch cacheConfig (FeedUri url) content

//     Assert.True(File.Exists filePath, "Expected cache file to be created")
//     Assert.Equal(content, File.ReadAllText filePath)
//     deleteFile filePath

// [<Fact>]
// let ``cacheSuccessfulFetch overwrites stale cache file with new content`` () =
//     let url = Uri "http://example.com/cache-overwrite-test"
//     let oldContent = "Old cached content"
//     let newContent = "New RSS content"
//     let filePath = Path.Combine(cacheConfig.Dir, convertUrlToValidFilename url)
//     createOutdatedCache filePath oldContent

//     cacheSuccessfulFetch cacheConfig (FeedUri url) newContent

//     Assert.Equal(newContent, File.ReadAllText filePath)
//     deleteFile filePath

[<Fact>]
let ``Test requestUrls skips invalid URLs in log file`` () =
    let filename = OsPath "test_invalid_urls.txt"

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
            uniqueValidRequestLogUrls filename
        with _ ->
            [||]

    Assert.Contains(Uri "https://valid-url.com/feed1", urls)
    Assert.Contains(Uri "https://valid-url.com/feed2", urls)
    Assert.DoesNotContain(Uri "ftp://unsupported-protocol.com/feed3", urls)
    Assert.Equal(2, Array.length urls)
    File.Delete filename
