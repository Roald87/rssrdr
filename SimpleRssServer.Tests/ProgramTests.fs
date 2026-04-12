module SimpleRssServer.Tests.ProgramTests

open Microsoft.Extensions.Logging.Abstractions
open System
open System.IO
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Xunit

open SimpleRssServer.Cache
open SimpleRssServer.Config
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.DomainModel
open SimpleRssServer.MemoryCache
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
    OsDirectory.create cacheDir

    { Dir = cacheDir
      Expiration = TimeSpan.FromHours 1.0 }

let createOutdatedCache (cachePath: OsPath) (content: string) =
    OsFile.writeAllText cachePath content
    let cacheConfig = makeCacheConfig ()
    let cacheAge = DateTime.Now - 2.0 * cacheConfig.Expiration
    OsFile.setLastWriteTime cachePath cacheAge

let makeTempLogPath () =
    OsPath(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt"))

let makeMemCache () = InMemoryCache NullLogger.Instance

[<Fact>]
let ``processRssRequest fetches feed, returns all articles, and writes cache`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 5
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()
    let logPath = makeTempLogPath ()
    let client = httpOkClient xmlContent
    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache logPath $"?rss={feedUrl}"

    // Assert
    Assert.Equal(articleCount, articles.Length)
    Assert.Equal(DummyXmlFeedFactory.articleTitle 1, articles[0].Title)
    Assert.Equal(DummyXmlFeedFactory.articleTitle articleCount, articles[articles.Length - 1].Title)

    let expectedCachePath =
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    Assert.True(OsFile.exists expectedCachePath, "Expected cache file to be written")
    Assert.Equal(xmlContent, OsFile.readAllText expectedCachePath)
    Assert.Contains(feedUrl, OsFile.readAllText logPath)
    Assert.Equal(Some articles, memCache.TryGet(feedUrl, cacheConfig.Expiration))

[<Fact>]
let ``processRssRequest uses cached content when HTTP returns 304 Not Modified`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 5
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()
    let logPath = makeTempLogPath ()

    let cachePath =
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    createOutdatedCache cachePath xmlContent

    let handler =
        new MockHttpMessageHandler(fun _ -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)))

    let client = new HttpClient(handler)
    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache logPath $"?rss={feedUrl}"

    // Assert
    Assert.Equal(1, handler.CallCount)
    Assert.Equal(articleCount, articles.Length)
    Assert.Equal(DummyXmlFeedFactory.articleTitle 1, articles[0].Title)
    Assert.Equal(DummyXmlFeedFactory.articleTitle articleCount, articles[articles.Length - 1].Title)
    Assert.Contains(feedUrl, OsFile.readAllText logPath)
    Assert.Equal(Some articles, memCache.TryGet(feedUrl, cacheConfig.Expiration))

[<Fact>]
let ``processRssRequest clears failure file when HTTP returns 304`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let xmlContent = DummyXmlFeedFactory.create feedUrl 3
    let cacheConfig = makeCacheConfig ()

    let cachePath =
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    createOutdatedCache cachePath xmlContent

    let failure =
        { LastFailure = DateTimeOffset.Now.AddHours -3.0
          ConsecutiveFailures = 2 }

    OsFile.writeAllText (failureFilePath cachePath) (System.Text.Json.JsonSerializer.Serialize failure)

    let handler =
        new MockHttpMessageHandler(fun _ -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)))

    let client = new HttpClient(handler)
    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={feedUrl}"

    // Assert
    Assert.False(OsFile.exists (failureFilePath cachePath), "Expected failure file to be removed after 304")
    Assert.Equal(Some articles, memCache.TryGet(feedUrl, cacheConfig.Expiration))

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
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    OsFile.writeAllText cachePath xmlContent

    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest mockClientThrowsWhenCalled NullLogger.Instance cacheConfig memCache logPath $"?rss={feedUrl}"

    // Assert
    Assert.Equal(articleCount, articles.Length)
    Assert.Equal(DummyXmlFeedFactory.articleTitle 1, articles[0].Title)
    Assert.Equal(DummyXmlFeedFactory.articleTitle articleCount, articles[articles.Length - 1].Title)
    Assert.Contains(feedUrl, OsFile.readAllText logPath)
    Assert.Equal(Some articles, memCache.TryGet(feedUrl, cacheConfig.Expiration))

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
    let memCache = makeMemCache ()

    // Act & Assert — first call fetches both feeds
    let articles1 =
        processRssRequest client NullLogger.Instance cacheConfig memCache logPath query

    Assert.Equal(2, handler.CallCount)
    Assert.Equal(articleCount * 2, articles1.Length)
    Assert.Contains(feedUrl1, OsFile.readAllText logPath)
    Assert.Contains(feedUrl2, OsFile.readAllText logPath)

    // Second and third calls must read from disk cache — HTTP call count stays at 2
    let articles2 =
        processRssRequest client NullLogger.Instance cacheConfig memCache logPath query

    Assert.Equal(2, handler.CallCount)
    Assert.Equal(articleCount * 2, articles2.Length)

    let articles3 =
        processRssRequest client NullLogger.Instance cacheConfig memCache logPath query

    Assert.Equal(2, handler.CallCount)
    Assert.Equal(articleCount * 2, articles3.Length)

let timeoutHandler () =
    new MockHttpMessageHandler(fun _ ->
        Task.FromException<HttpResponseMessage>(TaskCanceledException "Simulated timeout"))

[<Fact>]
let ``processRssRequest shows stale cache articles and error article on HTTP timeout`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 3
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()

    let cachePath =
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    createOutdatedCache cachePath xmlContent

    let client = new HttpClient(timeoutHandler ())
    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={feedUrl}"

    // Assert: stale cached articles + one error article
    Assert.Equal(articleCount + 1, articles.Length)
    Assert.Equal(DummyXmlFeedFactory.articleTitle 1, articles[0].Title)
    Assert.Equal("Error", articles[articles.Length - 1].Title)
    Assert.Equal(None, memCache.TryGet(feedUrl, cacheConfig.Expiration))

[<Fact>]
let ``processRssRequest shows only error article on HTTP timeout with no cache`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let cacheConfig = makeCacheConfig ()
    let client = new HttpClient(timeoutHandler ())
    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={feedUrl}"

    // Assert: only one error article, no cache to fall back on
    Assert.Equal(1, articles.Length)
    Assert.Equal("Error", articles[0].Title)

    let cacheFiles =
        OsDirectory.getFiles cacheConfig.Dir
        |> Array.filter (fun f -> not (f.EndsWith ".failures"))

    Assert.Equal(0, cacheFiles.Length)
    Assert.Equal(None, memCache.TryGet(feedUrl, cacheConfig.Expiration))

[<Fact>]
let ``processRssRequest shows PreviousHttpRequestFailed and skips HTTP on second call when no cache exists`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let cacheConfig = makeCacheConfig ()

    let cachePath =
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    let handler =
        new MockHttpMessageHandler(fun _ ->
            Task.FromException<HttpResponseMessage>(TaskCanceledException "Simulated timeout"))

    let client = new HttpClient(handler)
    let memCache = makeMemCache ()

    // Act — first call: HTTP times out, failure file should be created
    let articles1 =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={feedUrl}"

    Assert.Equal(1, handler.CallCount)
    Assert.Equal(1, articles1.Length)
    Assert.Equal("Error", articles1[0].Title)
    Assert.True(OsFile.exists (failureFilePath cachePath), "Expected failure file to be created after HTTP error")

    // Act — second call: should skip HTTP due to backoff
    let articles2 =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={feedUrl}"

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
    let memCache = makeMemCache ()

    let cachePath =
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    createOutdatedCache cachePath xmlContent

    let handler =
        new MockHttpMessageHandler(fun _ ->
            Task.FromException<HttpResponseMessage>(TaskCanceledException "Simulated timeout"))

    let client = new HttpClient(handler)

    // Act — first call: HTTP times out, stale cache shown, failure file should be created
    let articles1 =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={feedUrl}"

    Assert.Equal(1, handler.CallCount)
    Assert.Equal(articleCount + 1, articles1.Length)
    Assert.Equal("Error", articles1[articles1.Length - 1].Title)
    Assert.True(OsFile.exists (failureFilePath cachePath), "Expected failure file to be created after HTTP error")

    // Act — second call: should skip HTTP due to backoff
    let articles2 =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={feedUrl}"

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
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    createOutdatedCache cachePath (DummyXmlFeedFactory.create feedUrl 1)

    // 2 consecutive failures → 2h backoff; LastFailure 3h ago → backoff has passed
    let failure =
        { LastFailure = DateTimeOffset.Now.AddHours -3.0
          ConsecutiveFailures = 2 }

    OsFile.writeAllText (failureFilePath cachePath) (JsonSerializer.Serialize failure)

    let client = httpOkClient newXml
    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={feedUrl}"

    // Assert — new content returned and failure file cleared
    Assert.Equal(articleCount, articles.Length)
    Assert.DoesNotContain(articles, fun a -> a.Title = "Error")

    Assert.False(
        OsFile.exists (failureFilePath cachePath),
        "Expected failure file to be cleared after successful retry"
    )

    Assert.Equal(Some articles, memCache.TryGet(feedUrl, cacheConfig.Expiration))

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
    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={htmlUrl}"

    // Assert
    Assert.Equal(1, articles.Length)
    Assert.Equal("Error", articles[0].Title)
    Assert.Equal(OsDirectory.getFiles(cacheConfig.Dir).Length, 0)
    Assert.Equal(None, memCache.TryGet(htmlUrl, cacheConfig.Expiration))

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
    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache logPath $"?rss={htmlUrl}"

    // Assert: articles from discovered feed, no error
    Assert.Equal(articleCount, articles.Length)
    Assert.DoesNotContain(articles, fun a -> a.Title = "Error")
    Assert.Contains(feedUrl, OsFile.readAllText logPath)
    Assert.Equal(Some articles, memCache.TryGet(feedUrl, cacheConfig.Expiration))

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
    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={htmlUrl}"

    let queryUrls = getFeedUrlQuery articles |> fun x -> x.GetValues "rss"

    // Assert: processed query contains the feed URL, not the original page URL
    Assert.Equal(1, queryUrls.Length)
    Assert.Equal(feedUrl, queryUrls[0])
    Assert.Equal(Some articles, memCache.TryGet(feedUrl, cacheConfig.Expiration))

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
    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache logPath $"?rss={htmlUrl}"

    // Assert: relative feed URL was resolved, articles returned with no error
    Assert.Contains(articles, fun a -> a.FeedUrl = resolvedFeedUrl)
    Assert.Equal(articleCount, articles.Length)
    Assert.DoesNotContain(articles, fun a -> a.Title = "Error")
    Assert.Equal(Some articles, memCache.TryGet(resolvedFeedUrl, cacheConfig.Expiration))

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
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl1))

    let cachePath2 =
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl2))

    let oneDayAgo = DateTime.Now.AddDays(-1.0)
    OsFile.writeAllText cachePath1 oldXml1
    OsFile.setLastWriteTime cachePath1 oneDayAgo
    OsFile.writeAllText cachePath2 oldXml2
    OsFile.setLastWriteTime cachePath2 oneDayAgo

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
    let memCache = makeMemCache ()

    // Act
    updateCache client NullLogger.Instance cacheConfig memCache [ Uri feedUrl1; Uri feedUrl2 ]

    // Assert — new content is written to both cache files
    Assert.Equal(newXml1, OsFile.readAllText cachePath1)
    Assert.Equal(newXml2, OsFile.readAllText cachePath2)

    // Assert — last modification time is updated beyond the old cache age
    Assert.True(OsFile.getLastWriteTime cachePath1 > oneDayAgo)
    Assert.True(OsFile.getLastWriteTime cachePath2 > oneDayAgo)
    Assert.True(memCache.TryGet(feedUrl1, cacheConfig.Expiration).IsSome)
    Assert.True(memCache.TryGet(feedUrl2, cacheConfig.Expiration).IsSome)

[<Fact>]
let ``updateCache updates file write time but keeps cache content when server returns 304`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let cacheConfig = makeCacheConfig ()

    let cachedXml = DummyXmlFeedFactory.create feedUrl 3

    let cachePath =
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    let oneDayAgo = DateTime.Now.AddDays(-1.0)
    OsFile.writeAllText cachePath cachedXml
    OsFile.setLastWriteTime cachePath oneDayAgo

    let handler =
        new MockHttpMessageHandler(fun _ -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)))

    let client = new HttpClient(handler)
    let memCache = makeMemCache ()

    // Act
    updateCache client NullLogger.Instance cacheConfig memCache [ Uri feedUrl ]

    // Assert — cache content is unchanged
    Assert.Equal(cachedXml, OsFile.readAllText cachePath)

    // Assert — last modification time is updated
    Assert.True(OsFile.getLastWriteTime cachePath > oneDayAgo)
    Assert.Equal(None, memCache.TryGet(feedUrl, cacheConfig.Expiration))

[<Fact>]
let ``updateCache fetches and saves feed when no cache file exists`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let cacheConfig = makeCacheConfig ()
    let xmlContent = DummyXmlFeedFactory.create feedUrl 3

    let cachePath =
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    // No cache file created — absence is the precondition
    Assert.False(OsFile.exists cachePath)

    let client = httpOkClient xmlContent
    let memCache = makeMemCache ()

    // Act
    updateCache client NullLogger.Instance cacheConfig memCache [ Uri feedUrl ]

    // Assert — cache file is created with fetched content
    Assert.True(OsFile.exists cachePath)
    Assert.Equal(xmlContent, OsFile.readAllText cachePath)
    Assert.True(memCache.TryGet(feedUrl, cacheConfig.Expiration).IsSome)

[<Fact>]
let ``updateCache skips HTTP request and does not touch file when cache is still fresh`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let cacheConfig = makeCacheConfig ()
    let cachedXml = DummyXmlFeedFactory.create feedUrl 3

    let cachePath =
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    let halfExpiration = cacheConfig.Expiration / 2.0
    let recentWriteTime = DateTime.Now - halfExpiration
    OsFile.writeAllText cachePath cachedXml
    OsFile.setLastWriteTime cachePath recentWriteTime

    let handler =
        new MockHttpMessageHandler(fun _ -> failwith "HTTP request not expected")

    let client = new HttpClient(handler)
    let memCache = makeMemCache ()

    // Act
    updateCache client NullLogger.Instance cacheConfig memCache [ Uri feedUrl ]

    // Assert — no HTTP request was made
    Assert.Equal(0, handler.CallCount)

    // Assert — cache content and write time are unchanged
    Assert.Equal(cachedXml, OsFile.readAllText cachePath)
    Assert.Equal(recentWriteTime, OsFile.getLastWriteTime cachePath)
    Assert.Equal(None, memCache.TryGet(feedUrl, cacheConfig.Expiration))

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
    let memCache = makeMemCache ()

    // Act
    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache logPath $"?rss={htmlUrl}"

    // Assert: articles from both discovered feeds, no error
    Assert.Equal(articleCount * 2, articles.Length)
    Assert.DoesNotContain(articles, fun a -> a.Title = "Error")
    Assert.Contains(feedUrl1, OsFile.readAllText logPath)
    Assert.Contains(feedUrl2, OsFile.readAllText logPath)
    Assert.True(memCache.TryGet(feedUrl1, cacheConfig.Expiration).IsSome)
    Assert.True(memCache.TryGet(feedUrl2, cacheConfig.Expiration).IsSome)

[<Fact>]
let ``processRssRequest serves articles from memory cache and skips HTTP`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 3
    let cacheConfig = makeCacheConfig ()
    let memCache = makeMemCache ()

    let cachedArticles =
        [ 1..articleCount ]
        |> List.map (fun i ->
            { PostDate = Some DateTime.Now
              Title = DummyXmlFeedFactory.articleTitle i
              ArticleUrl = $"{feedUrl}/article/{i}"
              FeedUrl = feedUrl
              Text = "" })

    memCache.Set(feedUrl, cachedArticles)

    // Act — HTTP client throws if called
    let result =
        processRssRequest
            mockClientThrowsWhenCalled
            NullLogger.Instance
            cacheConfig
            memCache
            (makeTempLogPath ())
            $"?rss={feedUrl}"

    // Assert: articles served from memory, no HTTP call
    Assert.Equal(articleCount, result.Length)

[<Fact>]
let ``processRssRequest falls through to disk cache when memory cache entry is stale`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 3
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()
    let memCache = makeMemCache ()

    // Pre-populate memory cache with expired entry (zero expiration → always stale)
    memCache.Set(feedUrl, [])

    // Write disk cache and backdate its write time so the condition
    // (DateTimeOffset.Now - modTime) <= TimeSpan.Zero evaluates as fresh
    let cachePath =
        OsPath.combine cacheConfig.Dir (convertUrlToValidFilename (Uri feedUrl))

    OsFile.writeAllText cachePath xmlContent
    OsFile.setLastWriteTime cachePath (DateTime.Now.AddSeconds 5.0)

    // Act — HTTP client throws if called (must serve from disk, not HTTP)
    let articles =
        processRssRequest
            mockClientThrowsWhenCalled
            NullLogger.Instance
            { cacheConfig with
                Expiration = TimeSpan.Zero }
            memCache
            (makeTempLogPath ())
            $"?rss={feedUrl}"

    // Assert: articles come from disk cache (not empty stale memory entry), no HTTP call
    Assert.Equal(articleCount, articles.Length)
    Assert.DoesNotContain(articles, fun a -> a.Title = "Error")

[<Fact>]
let ``processRssRequest skips HTTP on second call when sharing InMemoryCache across requests`` () =
    // Arrange
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let articleCount = 3
    let xmlContent = DummyXmlFeedFactory.create feedUrl articleCount
    let cacheConfig = makeCacheConfig ()
    let memCache = makeMemCache ()

    let handler =
        new MockHttpMessageHandler(fun _ ->
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(xmlContent)
            Task.FromResult response)

    let client = new HttpClient(handler)

    // Act — first call fetches from HTTP and populates memory cache
    let articles1 =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={feedUrl}"

    Assert.Equal(1, handler.CallCount)
    Assert.Equal(articleCount, articles1.Length)

    // Act — second call with same InMemoryCache hits memory, no HTTP
    let articles2 =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={feedUrl}"

    Assert.Equal(1, handler.CallCount)
    Assert.Equal(articleCount, articles2.Length)

let makeArticle feedUrl =
    { PostDate = None
      Title = ""
      ArticleUrl = ""
      FeedUrl = feedUrl
      Text = "" }

let getSortedQueryUrls (q: Query) = q.GetValues "rss" |> List.sort

// Scenario 1: user enters example.com/feed (no scheme) → stored as example.com/feed, no redirect
[<Fact>]
let ``handleRequest keeps no-scheme url in query and does not redirect`` () =
    let articles = [ makeArticle "https://example.com/feed" ]
    let originalQuery = Query.Create "?rss=example.com/feed"
    let processedQuery = buildProcessedQuery articles
    Assert.Equal("example.com/feed", processedQuery.GetValues("rss")[0])
    Assert.Equal<string list>(getSortedQueryUrls originalQuery, getSortedQueryUrls processedQuery)

// Scenario 2: user enters http://example.com/feed → stored as http://example.com/feed, no redirect
[<Fact>]
let ``handleRequest keeps http scheme in query and does not redirect`` () =
    let articles = [ makeArticle "http://example.com/feed" ]
    let originalQuery = Query.Create "?rss=http://example.com/feed"
    let processedQuery = buildProcessedQuery articles
    Assert.Equal("http://example.com/feed", processedQuery.GetValues("rss")[0])
    Assert.Equal<string list>(getSortedQueryUrls originalQuery, getSortedQueryUrls processedQuery)

// Scenario 3: user enters https://example.com/feed → stripped to example.com/feed via redirect
[<Fact>]
let ``handleRequest strips https scheme from query via redirect`` () =
    let articles = [ makeArticle "https://example.com/feed" ]
    let originalQuery = Query.Create "?rss=https://example.com/feed"
    let processedQuery = buildProcessedQuery articles
    Assert.Equal("example.com/feed", processedQuery.GetValues("rss")[0])
    Assert.NotEqual<string list>(getSortedQueryUrls originalQuery, getSortedQueryUrls processedQuery)

// Scenario 4: user enters example.com/ → discovery finds https://example.com/feed → redirect to example.com/feed
[<Fact>]
let ``handleRequest strips https from discovered feed url in redirect`` () =
    let pagePath = $"example.com/page/{Guid.NewGuid()}"
    let htmlUrl = $"https://{pagePath}"
    let feedGuid = Guid.NewGuid()
    let feedUrl = $"https://example.com/feed/{feedGuid}"
    let xmlContent = DummyXmlFeedFactory.create feedUrl 3
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
    let memCache = makeMemCache ()

    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={pagePath}"

    let result = buildProcessedQuery articles |> fun q -> q.GetValues "rss"
    Assert.Equal(1, result.Length)
    Assert.Equal($"example.com/feed/{feedGuid}", result[0])

// Scenario 5: user enters example.com/ → discovery finds http://example.com/feed → redirect to http://example.com/feed
[<Fact>]
let ``handleRequest keeps http scheme for discovered feed url in redirect`` () =
    let pagePath = $"example.com/page/{Guid.NewGuid()}"
    let htmlUrl = $"https://{pagePath}"
    let feedGuid = Guid.NewGuid()
    let feedUrl = $"http://example.com/feed/{feedGuid}"
    let xmlContent = DummyXmlFeedFactory.create feedUrl 3
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
    let memCache = makeMemCache ()

    let articles =
        processRssRequest client NullLogger.Instance cacheConfig memCache (makeTempLogPath ()) $"?rss={pagePath}"

    let result = buildProcessedQuery articles |> fun q -> q.GetValues "rss"
    Assert.Equal(1, result.Length)
    Assert.Equal(feedUrl, result[0])
