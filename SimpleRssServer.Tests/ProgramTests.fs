module SimpleRssServer.Tests.ProgramTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Xunit

open SimpleRssServer.Cache
open SimpleRssServer.Config
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.DomainModel
open Program
open TestHelpers

let httpClientWithResponses (responses: Map<string, HttpResponseMessage>) =
    let handler =
        new MockHttpMessageHandler(fun request ->
            let url = request.RequestUri.AbsoluteUri

            match Map.tryFind url responses with
            | Some response -> Task.FromResult response
            | None -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))

    new HttpClient(handler)

let makeCacheConfig () =
    let cacheDir = OsPath(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()))
    Directory.CreateDirectory cacheDir

    { Dir = cacheDir
      Expiration = TimeSpan.FromHours 1.0 }

let createOutdatedCache (cachePath: OsPath) (content: string) =
    File.WriteAllText(cachePath, content)
    let cacheConfig = makeCacheConfig ()
    let cacheAge = DateTime.Now - 2.0 * cacheConfig.Expiration
    File.SetLastWriteTime(cachePath, cacheAge)

let makeTempLogPath () =
    OsPath(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt"))

[<Fact>]
let ``processRssRequest fetches feed, returns all articles, and writes cache`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 5
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()
    let logPath = makeTempLogPath ()
    let client = httpOkClient xmlContent

    // Act
    let _, articles = processRssRequest client cacheConfig logPath $"?rss={feedUrl}"

    // Assert
    Assert.Equal(articleCount, articles.Length)
    Assert.Equal(DummyXmlFeedFactory.articleTitle 1, articles[0].Title)
    Assert.Equal(DummyXmlFeedFactory.articleTitle articleCount, articles[articles.Length - 1].Title)

    let expectedCachePath =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl))

    Assert.True(File.Exists expectedCachePath, "Expected cache file to be written")
    Assert.Equal(xmlContent, File.ReadAllText expectedCachePath)
    Assert.Contains(feedUrl, File.ReadAllText logPath)

[<Fact>]
let ``processRssRequest uses cached content when HTTP returns 304 Not Modified`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 5
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()
    let logPath = makeTempLogPath ()

    let cachePath =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl))

    createOutdatedCache cachePath xmlContent

    let handler =
        new MockHttpMessageHandler(fun _ -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)))

    let client = new HttpClient(handler)

    // Act
    let _, articles = processRssRequest client cacheConfig logPath $"?rss={feedUrl}"

    // Assert
    Assert.Equal(1, handler.CallCount)
    Assert.Equal(articleCount, articles.Length)
    Assert.Equal(DummyXmlFeedFactory.articleTitle 1, articles[0].Title)
    Assert.Equal(DummyXmlFeedFactory.articleTitle articleCount, articles[articles.Length - 1].Title)
    Assert.Contains(feedUrl, File.ReadAllText logPath)

[<Fact>]
let ``processRssRequest clears failure file when HTTP returns 304`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let xmlContent = DummyXmlFeedFactory.create feedUrl 3
    let cacheConfig = makeCacheConfig ()

    let cachePath =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl))

    createOutdatedCache cachePath xmlContent

    let failure =
        { LastFailure = DateTimeOffset.Now.AddHours -3.0
          ConsecutiveFailures = 2 }

    File.WriteAllText(failureFilePath cachePath, System.Text.Json.JsonSerializer.Serialize failure)

    let handler =
        new MockHttpMessageHandler(fun _ -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)))

    let client = new HttpClient(handler)

    // Act
    processRssRequest client cacheConfig (makeTempLogPath ()) $"?rss={feedUrl}"

    |> ignore

    // Assert
    Assert.False(File.Exists(failureFilePath cachePath), "Expected failure file to be removed after 304")

let mockClientThrowsWhenCalled =
    let handler =
        new MockHttpMessageHandler(fun _ -> failwith "HTTP request not expected to be made")

    new HttpClient(handler)

[<Fact>]
let ``processRssRequest serves articles from cache and makes no HTTP request`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 5
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()
    let logPath = makeTempLogPath ()

    let cachePath =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl))

    File.WriteAllText(cachePath, xmlContent)

    // Act
    let _, articles =
        processRssRequest mockClientThrowsWhenCalled cacheConfig logPath $"?rss={feedUrl}"

    // Assert
    Assert.Equal(articleCount, articles.Length)
    Assert.Equal(DummyXmlFeedFactory.articleTitle 1, articles[0].Title)
    Assert.Equal(DummyXmlFeedFactory.articleTitle articleCount, articles[articles.Length - 1].Title)
    Assert.Contains(feedUrl, File.ReadAllText logPath)

[<Fact>]
let ``processRssRequest fetches via HTTP only on first call, subsequent calls read from cache`` () =
    // Arrange
    let feedUrl1 = $"https://example.com/feed/{Guid.NewGuid()}"
    let feedUrl2 = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 3
    let xml1 = DummyXmlFeedFactory.create feedUrl1 articleCount
    let xml2 = DummyXmlFeedFactory.create feedUrl2 articleCount
    let cacheConfig = makeCacheConfig ()

    let handler =
        new MockHttpMessageHandler(fun request ->
            let xml =
                if request.RequestUri.AbsoluteUri = feedUrl1 then
                    xml1
                elif request.RequestUri.AbsoluteUri = feedUrl2 then
                    xml2
                else
                    failwith $"Unexpected URL: {request.RequestUri.AbsoluteUri}"

            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(xml)
            Task.FromResult response)

    let client = new HttpClient(handler)
    let query = $"?rss={feedUrl1}&rss={feedUrl2}"
    let logPath = makeTempLogPath ()

    // Act & Assert — first call fetches both feeds
    let _, articles1 = processRssRequest client cacheConfig logPath query
    Assert.Equal(2, handler.CallCount)
    Assert.Equal(articleCount * 2, articles1.Length)
    Assert.Contains(feedUrl1, File.ReadAllText logPath)
    Assert.Contains(feedUrl2, File.ReadAllText logPath)

    // Second and third calls must read from cache — HTTP call count stays at 2
    let _, articles2 = processRssRequest client cacheConfig logPath query
    Assert.Equal(2, handler.CallCount)
    Assert.Equal(articleCount * 2, articles2.Length)

    let _, articles3 = processRssRequest client cacheConfig logPath query
    Assert.Equal(2, handler.CallCount)
    Assert.Equal(articleCount * 2, articles3.Length)

let timeoutHandler () =
    new MockHttpMessageHandler(fun _ ->
        Task.FromException<HttpResponseMessage>(Threading.Tasks.TaskCanceledException "Simulated timeout"))

[<Fact>]
let ``processRssRequest shows stale cache articles and error article on HTTP timeout`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 3
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()

    let cachePath =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl))

    createOutdatedCache cachePath xmlContent

    let client = new HttpClient(timeoutHandler ())

    // Act
    let _, articles =
        processRssRequest client cacheConfig (makeTempLogPath ()) $"?rss={feedUrl}"


    // Assert: stale cached articles + one error article
    Assert.Equal(articleCount + 1, articles.Length)
    Assert.Equal(DummyXmlFeedFactory.articleTitle 1, articles[0].Title)
    Assert.Equal("Error", articles[articles.Length - 1].Title)

[<Fact>]
let ``processRssRequest shows only error article on HTTP timeout with no cache`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let cacheConfig = makeCacheConfig ()
    let client = new HttpClient(timeoutHandler ())

    // Act
    let _, articles =
        processRssRequest client cacheConfig (makeTempLogPath ()) $"?rss={feedUrl}"


    // Assert: only one error article, no cache to fall back on
    Assert.Equal(1, articles.Length)
    Assert.Equal("Error", articles[0].Title)

    let cacheFiles =
        Directory.GetFiles(cacheConfig.Dir)
        |> Array.filter (fun f -> not (f.EndsWith ".failures"))

    Assert.Equal(0, cacheFiles.Length)

[<Fact>]
let ``processRssRequest shows PreviousHttpRequestFailed and skips HTTP on second call when no cache exists`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let cacheConfig = makeCacheConfig ()

    let cachePath =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl))

    let handler =
        new MockHttpMessageHandler(fun _ ->
            Task.FromException<HttpResponseMessage>(Threading.Tasks.TaskCanceledException "Simulated timeout"))

    let client = new HttpClient(handler)

    // Act — first call: HTTP times out, failure file should be created
    let _, articles1 =
        processRssRequest client cacheConfig (makeTempLogPath ()) $"?rss={feedUrl}"


    Assert.Equal(1, handler.CallCount)
    Assert.Equal(1, articles1.Length)
    Assert.Equal("Error", articles1[0].Title)
    Assert.True(File.Exists(failureFilePath cachePath), "Expected failure file to be created after HTTP error")

    // Act — second call: should skip HTTP due to backoff
    let _, articles2 =
        processRssRequest client cacheConfig (makeTempLogPath ()) $"?rss={feedUrl}"


    // Assert: no new HTTP request made, error article reflects backoff
    Assert.Equal(1, handler.CallCount)
    Assert.Equal(1, articles2.Length)
    Assert.Equal("Error", articles2[0].Title)
    Assert.Contains("Retrying in", articles2[0].Text)

[<Fact>]
let ``processRssRequest shows PreviousHttpRequestFailedButPageCached and skips HTTP on second call when stale cache exists``
    ()
    =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 3
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()

    let cachePath =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl))

    createOutdatedCache cachePath xmlContent

    let handler =
        new MockHttpMessageHandler(fun _ ->
            Task.FromException<HttpResponseMessage>(Threading.Tasks.TaskCanceledException "Simulated timeout"))

    let client = new HttpClient(handler)

    // Act — first call: HTTP times out, stale cache shown, failure file should be created
    let _, articles1 =
        processRssRequest client cacheConfig (makeTempLogPath ()) $"?rss={feedUrl}"


    Assert.Equal(1, handler.CallCount)
    Assert.Equal(articleCount + 1, articles1.Length)
    Assert.Equal("Error", articles1[articles1.Length - 1].Title)
    Assert.True(File.Exists(failureFilePath cachePath), "Expected failure file to be created after HTTP error")

    // Act — second call: should skip HTTP due to backoff
    let _, articles2 =
        processRssRequest client cacheConfig (makeTempLogPath ()) $"?rss={feedUrl}"


    // Assert: no new HTTP request made, stale articles shown with backoff error
    Assert.Equal(1, handler.CallCount)
    Assert.Equal(articleCount + 1, articles2.Length)
    Assert.Equal("Error", articles2[articles2.Length - 1].Title)
    Assert.Contains("There is a saved version of the feed", articles2[articles2.Length - 1].Text)

[<Fact>]
let ``processRssRequest retries and clears failure file when backoff period has expired`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 3
    let newXml = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()

    let cachePath =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl))

    createOutdatedCache cachePath (DummyXmlFeedFactory.create feedUrl 1)

    // 2 consecutive failures → 2h backoff; LastFailure 3h ago → backoff has passed
    let failure =
        { LastFailure = DateTimeOffset.Now.AddHours -3.0
          ConsecutiveFailures = 2 }

    File.WriteAllText(failureFilePath cachePath, System.Text.Json.JsonSerializer.Serialize failure)

    let client = httpOkClient newXml

    // Act
    let _, articles =
        processRssRequest client cacheConfig (makeTempLogPath ()) $"?rss={feedUrl}"


    // Assert — new content returned and failure file cleared
    Assert.Equal(articleCount, articles.Length)
    Assert.DoesNotContain(articles, fun a -> a.Title = "Error")
    Assert.False(File.Exists(failureFilePath cachePath), "Expected failure file to be cleared after successful retry")

[<Fact>]
let ``processRssRequest shows error article when HTML page has no feed links`` () =
    // Arrange
    let htmlUrl = $"https://example.com/page/{Guid.NewGuid()}"

    let htmlContent =
        "<html><head><title>No feeds here</title></head><body></body></html>"

    let cacheConfig = makeCacheConfig ()

    let responses =
        Map.ofList
            [ htmlUrl,
              (let r = new HttpResponseMessage(HttpStatusCode.OK)
               r.Content <- new StringContent(htmlContent)
               r) ]

    let client = httpClientWithResponses responses

    // Act
    let _, articles =
        processRssRequest client cacheConfig (makeTempLogPath ()) $"?rss={htmlUrl}"


    // Assert
    Assert.Equal(1, articles.Length)
    Assert.Equal("Error", articles[0].Title)
    Assert.Equal(Directory.GetFiles(cacheConfig.Dir).Length, 0)

[<Fact>]
let ``processRssRequest shows articles when HTML page has single feed link`` () =
    // Arrange
    let htmlUrl = $"https://example.com/page/{Guid.NewGuid()}"
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 3
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()
    let logPath = makeTempLogPath ()

    let htmlContent =
        $"""<html><head><link rel="alternate" type="application/rss+xml" title="Feed" href="{feedUrl}"></head><body></body></html>"""

    let responses =
        Map.ofList
            [ htmlUrl,
              (let r = new HttpResponseMessage(HttpStatusCode.OK)
               r.Content <- new StringContent(htmlContent)
               r)
              feedUrl,
              (let r = new HttpResponseMessage(HttpStatusCode.OK)
               r.Content <- new StringContent(xmlContent)
               r) ]

    let client = httpClientWithResponses responses

    // Act
    let _, articles = processRssRequest client cacheConfig logPath $"?rss={htmlUrl}"

    // Assert: articles from discovered feed, no error
    Assert.Equal(articleCount, articles.Length)
    Assert.DoesNotContain(articles, fun a -> a.Title = "Error")
    Assert.Contains(feedUrl, File.ReadAllText logPath)

[<Fact>]
let ``processRssRequest returns processed query with discovered feed URL instead of page URL`` () =
    // Arrange
    let htmlUrl = $"https://example.com/page/{Guid.NewGuid()}"
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 3
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()

    let htmlContent =
        $"""<html><head><link rel="alternate" type="application/rss+xml" title="Feed" href="{feedUrl}"></head><body></body></html>"""

    let responses =
        Map.ofList
            [ htmlUrl,
              (let r = new HttpResponseMessage(HttpStatusCode.OK)
               r.Content <- new StringContent(htmlContent)
               r)
              feedUrl,
              (let r = new HttpResponseMessage(HttpStatusCode.OK)
               r.Content <- new StringContent(xmlContent)
               r) ]

    let client = httpClientWithResponses responses

    // Act
    let processedQuery, _ =
        processRssRequest client cacheConfig (makeTempLogPath ()) $"?rss={htmlUrl}"

    // Assert: processed query contains the feed URL, not the original page URL
    let queryUrls = processedQuery.GetValues "rss"
    Assert.Equal(1, queryUrls.Length)
    Assert.Equal(feedUrl, queryUrls[0])

[<Fact>]
let ``processRssRequest resolves relative feed URL in discovered feed against original page URL`` () =
    // Arrange
    let htmlUrl = $"https://example.com/page/{Guid.NewGuid()}"
    let relativeFeedPath = "/feed/relative"
    let resolvedFeedUrl = $"https://example.com{relativeFeedPath}"
    let articleCount = 3
    let xmlContent = DummyXmlFeedFactory.create resolvedFeedUrl articleCount
    let cacheConfig = makeCacheConfig ()
    let logPath = makeTempLogPath ()

    let htmlContent =
        $"""<html><head><link rel="alternate" type="application/rss+xml" title="Feed" href="{relativeFeedPath}"></head><body></body></html>"""

    let responses =
        Map.ofList
            [ htmlUrl,
              (let r = new HttpResponseMessage(HttpStatusCode.OK)
               r.Content <- new StringContent(htmlContent)
               r)
              resolvedFeedUrl,
              (let r = new HttpResponseMessage(HttpStatusCode.OK)
               r.Content <- new StringContent(xmlContent)
               r) ]

    let client = httpClientWithResponses responses

    // Act
    let _, articles = processRssRequest client cacheConfig logPath $"?rss={htmlUrl}"

    // Assert: relative feed URL was resolved, articles returned with no error
    Assert.Contains(articles, fun a -> a.FeedUrl = resolvedFeedUrl)
    Assert.Equal(articleCount, articles.Length)
    Assert.DoesNotContain(articles, fun a -> a.Title = "Error")

[<Fact>]
let ``updateCache fetches new feed content and overwrites stale cache files`` () =
    // Arrange
    let feedUrl1 = $"https://example.com/feed/{Guid.NewGuid()}"
    let feedUrl2 = $"https://example.com/feed/{Guid.NewGuid()}"
    let cacheConfig = makeCacheConfig ()

    let oldXml1 = DummyXmlFeedFactory.create feedUrl1 2
    let oldXml2 = DummyXmlFeedFactory.create feedUrl2 2
    let newXml1 = DummyXmlFeedFactory.create feedUrl1 5
    let newXml2 = DummyXmlFeedFactory.create feedUrl2 5

    let cachePath1 =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl1))

    let cachePath2 =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl2))

    let oneDayAgo = DateTime.Now.AddDays(-1.0)
    File.WriteAllText(cachePath1, oldXml1)
    File.SetLastWriteTime(cachePath1, oneDayAgo)
    File.WriteAllText(cachePath2, oldXml2)
    File.SetLastWriteTime(cachePath2, oneDayAgo)

    let handler =
        new MockHttpMessageHandler(fun request ->
            let xml =
                if request.RequestUri.AbsoluteUri = feedUrl1 then
                    newXml1
                elif request.RequestUri.AbsoluteUri = feedUrl2 then
                    newXml2
                else
                    failwith $"Unexpected URL: {request.RequestUri.AbsoluteUri}"

            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(xml)
            Task.FromResult response)

    let client = new HttpClient(handler)

    // Act
    updateCache client cacheConfig [| Uri feedUrl1; Uri feedUrl2 |]

    // Assert — new content is written to both cache files
    Assert.Equal(newXml1, File.ReadAllText cachePath1)
    Assert.Equal(newXml2, File.ReadAllText cachePath2)

    // Assert — last modification time is updated beyond the old cache age
    Assert.True(File.GetLastWriteTime cachePath1 > oneDayAgo)
    Assert.True(File.GetLastWriteTime cachePath2 > oneDayAgo)

[<Fact>]
let ``updateCache updates file write time but keeps cache content when server returns 304`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let cacheConfig = makeCacheConfig ()

    let cachedXml = DummyXmlFeedFactory.create feedUrl 3

    let cachePath =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl))

    let oneDayAgo = DateTime.Now.AddDays(-1.0)
    File.WriteAllText(cachePath, cachedXml)
    File.SetLastWriteTime(cachePath, oneDayAgo)

    let handler =
        new MockHttpMessageHandler(fun _ -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)))

    let client = new HttpClient(handler)

    // Act
    updateCache client cacheConfig [| Uri feedUrl |]

    // Assert — cache content is unchanged
    Assert.Equal(cachedXml, File.ReadAllText cachePath)

    // Assert — last modification time is updated
    Assert.True(File.GetLastWriteTime cachePath > oneDayAgo)

[<Fact>]
let ``updateCache fetches and saves feed when no cache file exists`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let cacheConfig = makeCacheConfig ()
    let xmlContent = DummyXmlFeedFactory.create feedUrl 3

    let cachePath =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl))

    // No cache file created — absence is the precondition
    Assert.False(File.Exists cachePath)

    let client = httpOkClient xmlContent

    // Act
    updateCache client cacheConfig [| Uri feedUrl |]

    // Assert — cache file is created with fetched content
    Assert.True(File.Exists cachePath)
    Assert.Equal(xmlContent, File.ReadAllText cachePath)

[<Fact>]
let ``updateCache skips HTTP request and does not touch file when cache is still fresh`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let cacheConfig = makeCacheConfig ()
    let cachedXml = DummyXmlFeedFactory.create feedUrl 3

    let cachePath =
        Path.Combine(cacheConfig.Dir, convertUrlToValidFilename (Uri feedUrl))

    let halfExpiration = cacheConfig.Expiration / 2.0
    let recentWriteTime = DateTime.Now - halfExpiration
    File.WriteAllText(cachePath, cachedXml)
    File.SetLastWriteTime(cachePath, recentWriteTime)

    let handler =
        new MockHttpMessageHandler(fun _ -> failwith "HTTP request not expected")

    let client = new HttpClient(handler)

    // Act
    updateCache client cacheConfig [| Uri feedUrl |]

    // Assert — no HTTP request was made
    Assert.Equal(0, handler.CallCount)

    // Assert — cache content and write time are unchanged
    Assert.Equal(cachedXml, File.ReadAllText cachePath)
    Assert.Equal(recentWriteTime, File.GetLastWriteTime cachePath)

[<Fact>]
let ``processRssRequest shows articles from both feeds when HTML page has two feed links`` () =
    // Arrange
    let htmlUrl = $"https://example.com/page/{Guid.NewGuid()}"
    let feedUrl1 = $"https://example.com/feed/{Guid.NewGuid()}"
    let feedUrl2 = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 3
    let xml1 = DummyXmlFeedFactory.create feedUrl1 articleCount
    let xml2 = DummyXmlFeedFactory.create feedUrl2 articleCount
    let cacheConfig = makeCacheConfig ()
    let logPath = makeTempLogPath ()

    let htmlContent =
        $"""<html><head>
        <link rel="alternate" type="application/rss+xml" title="Feed 1" href="{feedUrl1}">
        <link rel="alternate" type="application/atom+xml" title="Feed 2" href="{feedUrl2}">
        </head><body></body></html>"""

    let responses =
        Map.ofList
            [ htmlUrl,
              (let r = new HttpResponseMessage(HttpStatusCode.OK)
               r.Content <- new StringContent(htmlContent)
               r)
              feedUrl1,
              (let r = new HttpResponseMessage(HttpStatusCode.OK)
               r.Content <- new StringContent(xml1)
               r)
              feedUrl2,
              (let r = new HttpResponseMessage(HttpStatusCode.OK)
               r.Content <- new StringContent(xml2)
               r) ]

    let client = httpClientWithResponses responses

    // Act
    let _, articles = processRssRequest client cacheConfig logPath $"?rss={htmlUrl}"

    // Assert: articles from both discovered feeds, no error
    Assert.Equal(articleCount * 2, articles.Length)
    Assert.DoesNotContain(articles, fun a -> a.Title = "Error")
    Assert.Contains(feedUrl1, File.ReadAllText logPath)
    Assert.Contains(feedUrl2, File.ReadAllText logPath)
