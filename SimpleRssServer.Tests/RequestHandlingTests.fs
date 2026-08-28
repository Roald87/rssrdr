module SimpleRssServer.Tests.RequestHandlingTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit

open SimpleRssServer.AppContext
open SimpleRssServer.Config
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.MemoryCache
open SimpleRssServer.RequestHandlers
open TestHelpers

let private freePort () =
    let probe = new TcpListener(IPAddress.Loopback, 0)
    probe.Start()
    let port = (probe.LocalEndpoint :?> IPEndPoint).Port
    probe.Stop()
    port

/// Runs `handleRequest` behind a real HttpListener bound to a loopback port, with a
/// mock HttpClient (feed url -> RSS xml) and throwaway cache/collections/log locations.
/// Requests are served one at a time, mirroring the production server loop.
type private TestServer(feeds: Map<string, string>) =
    let port = freePort ()
    let baseUrl = $"http://localhost:{port}"
    let cacheDir = new TempDir()
    let collectionsDir = new TempDir()
    let logFile = new TempPath()

    let handler =
        new MockHttpMessageHandler(fun request ->
            match Map.tryFind request.RequestUri.AbsoluteUri feeds with
            | Some xml ->
                let response = new HttpResponseMessage(HttpStatusCode.OK)
                response.Content <- new StringContent(xml)
                Task.FromResult response
            | None -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))

    let appCtx: AppContext =
        { Client = new HttpClient(handler)
          Logger = NullLogger.Instance
          CacheConfig =
            { Dir = cacheDir.Path
              Expiration = TimeSpan.FromHours 1.0 }
          MemCache = InMemoryCache NullLogger.Instance
          CollectionsDir = collectionsDir.Path
          RequestLogPath = logFile.Path }

    let listener = new HttpListener()
    do listener.Prefixes.Add(baseUrl + "/")
    do listener.Start()

    let rec loop () =
        async {
            let! next = listener.GetContextAsync() |> Async.AwaitTask |> Async.Catch

            match next with
            | Choice1Of2 httpCtx ->
                try
                    do! handleRequest appCtx httpCtx
                with _ ->
                    try
                        httpCtx.Response.Abort()
                    with _ ->
                        ()

                return! loop ()
            | Choice2Of2 _ -> return () // listener stopped
        }

    do Async.Start(loop ())

    member _.BaseUrl = baseUrl
    member _.CollectionsDir = collectionsDir.Path
    member _.RequestLogPath = logFile.Path

    interface IDisposable with
        member _.Dispose() =
            listener.Stop()
            listener.Close()
            (cacheDir :> IDisposable).Dispose()
            (collectionsDir :> IDisposable).Dispose()
            (logFile :> IDisposable).Dispose()

let private newClient () =
    let client = new HttpClient(new HttpClientHandler(AllowAutoRedirect = false))
    client.Timeout <- TimeSpan.FromSeconds 10.0
    client

let private get (client: HttpClient) (url: string) =
    client.GetAsync url |> Async.AwaitTask |> Async.RunSynchronously

let private post (client: HttpClient) (url: string) (form: string) =
    let content =
        new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded")

    client.PostAsync(url, content) |> Async.AwaitTask |> Async.RunSynchronously

let private body (response: HttpResponseMessage) =
    response.Content.ReadAsStringAsync()
    |> Async.AwaitTask
    |> Async.RunSynchronously

let private siteFile name =
    File.ReadAllText(Path.Combine("site", name))

let private siteFileBytes name =
    File.ReadAllBytes(Path.Combine("site", name))

let private bodyBytes (response: HttpResponseMessage) =
    response.Content.ReadAsByteArrayAsync()
    |> Async.AwaitTask
    |> Async.RunSynchronously

let private landingMarker = "The simplest RSS reader on the planet"

[<Fact>]
let ``GET / serves the landing page`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let response = get client $"{server.BaseUrl}/"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Contains(landingMarker, body response)

[<Fact>]
let ``an unknown path serves the landing page`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let response = get client $"{server.BaseUrl}/not/a/route"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Contains(landingMarker, body response)

[<Fact>]
let ``GET /robots.txt serves the static file`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let response = get client $"{server.BaseUrl}/robots.txt"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Equal(siteFile "robots.txt", body response)

[<Fact>]
let ``GET /sitemap.xml serves the static file`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let response = get client $"{server.BaseUrl}/sitemap.xml"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Equal(siteFile "sitemap.xml", body response)

[<Fact>]
let ``GET /apple-touch-icon.png serves the static file`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let response = get client $"{server.BaseUrl}/apple-touch-icon.png"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Equal("image/png", response.Content.Headers.ContentType.MediaType)
    Assert.Equal<byte[]>(siteFileBytes "apple-touch-icon.png", bodyBytes response)

[<Fact>]
let ``GET /config.html serves the config page`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let response = get client $"{server.BaseUrl}/config.html"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Contains("<textarea", body response)

[<Fact>]
let ``GET /?rss= fetches and renders the feed`` () =
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let xml = DummyXmlFeedFactory.create feedUrl 3
    use server = new TestServer(Map [ feedUrl, xml ])
    use client = newClient ()

    let response =
        get client $"{server.BaseUrl}/?rss={FeedUri.removeHttpsScheme feedUrl}"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let rendered = body response
    Assert.Contains(DummyXmlFeedFactory.articleTitle 1, rendered)
    Assert.Contains(DummyXmlFeedFactory.articleTitle 3, rendered)

[<Fact>]
let ``GET /?rss= with an https url redirects via meta refresh to the scheme-stripped url`` () =
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let xml = DummyXmlFeedFactory.create feedUrl 3
    use server = new TestServer(Map [ feedUrl, xml ])
    use client = newClient ()

    let response = get client $"{server.BaseUrl}/?rss={feedUrl}"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let rendered = body response
    Assert.Contains("http-equiv=\"refresh\"", rendered)
    Assert.Contains($"rss={FeedUri.removeHttpsScheme feedUrl}", rendered)

[<Fact>]
let ``GET /shuffle?rss= renders the feed articles`` () =
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let xml = DummyXmlFeedFactory.create feedUrl 3
    use server = new TestServer(Map [ feedUrl, xml ])
    use client = newClient ()

    let response =
        get client $"{server.BaseUrl}/shuffle?rss={FeedUri.removeHttpsScheme feedUrl}"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Contains(DummyXmlFeedFactory.articleTitle 2, body response)

[<Fact>]
let ``a rendered feed is recorded in the request log`` () =
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let xml = DummyXmlFeedFactory.create feedUrl 2
    use server = new TestServer(Map [ feedUrl, xml ])
    use client = newClient ()

    get client $"{server.BaseUrl}/?rss={FeedUri.removeHttpsScheme feedUrl}"
    |> ignore

    Assert.Contains(feedUrl, OsFile.readAllText server.RequestLogPath)

[<Fact>]
let ``DELETE /s/<id> falls through to the landing page`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let request =
        new HttpRequestMessage(HttpMethod.Delete, $"{server.BaseUrl}/s/somecollection")

    let response = client.SendAsync request |> Async.AwaitTask |> Async.RunSynchronously

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Contains(landingMarker, body response)

[<Fact>]
let ``POST /s creates a collection, redirects, and writes the file`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let response =
        post client $"{server.BaseUrl}/s" "rss=example.com/a&rss=example.com/b"

    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
    let location = response.Headers.Location.OriginalString
    Assert.StartsWith("/s/", location)

    let id = location.Substring 3

    let saved =
        OsFile.readAllLines (OsPath.join server.CollectionsDir (id + ".txt"))
        |> Array.toList

    Assert.Equal<string list>([ "example.com/a"; "example.com/b" ], saved)

[<Fact>]
let ``a created collection can be viewed at /s/<id>`` () =
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let xml = DummyXmlFeedFactory.create feedUrl 3
    use server = new TestServer(Map [ feedUrl, xml ])
    use client = newClient ()

    let create =
        post client $"{server.BaseUrl}/s" $"rss={FeedUri.removeHttpsScheme feedUrl}"

    let location = create.Headers.Location.OriginalString

    let response = get client $"{server.BaseUrl}{location}"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Contains(DummyXmlFeedFactory.articleTitle 1, body response)

[<Fact>]
let ``GET /s/<id>/shuffle renders the collection's feeds`` () =
    let feedUrl = $"https://example.com/feed/{Guid.NewGuid()}"
    let xml = DummyXmlFeedFactory.create feedUrl 3
    use server = new TestServer(Map [ feedUrl, xml ])
    use client = newClient ()

    let create =
        post client $"{server.BaseUrl}/s" $"rss={FeedUri.removeHttpsScheme feedUrl}"

    let id = create.Headers.Location.OriginalString.Substring 3

    let response = get client $"{server.BaseUrl}/s/{id}/shuffle"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Contains(DummyXmlFeedFactory.articleTitle 1, body response)

[<Fact>]
let ``POST /s/<id> updates an existing collection`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let create = post client $"{server.BaseUrl}/s" "rss=example.com/old"
    let location = create.Headers.Location.OriginalString

    let update =
        post client $"{server.BaseUrl}{location}" "rss=example.com/new1&rss=example.com/new2"

    Assert.Equal(HttpStatusCode.Redirect, update.StatusCode)

    let id = location.Substring 3

    let saved =
        OsFile.readAllLines (OsPath.join server.CollectionsDir (id + ".txt"))
        |> Array.toList

    Assert.Equal<string list>([ "example.com/new1"; "example.com/new2" ], saved)

[<Fact>]
let ``GET /config.html?s=<id> prefills the collection's feeds and checks the save box`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let create =
        post client $"{server.BaseUrl}/s" "rss=example.com/one&rss=example.com/two"

    let id = create.Headers.Location.OriginalString.Substring 3

    let response = get client $"{server.BaseUrl}/config.html?s={id}"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let rendered = body response
    Assert.Contains("example.com/one", rendered)
    Assert.Contains("example.com/two", rendered)
    Assert.Contains("""id="saveCollection" checked""", rendered)

[<Fact>]
let ``GET /s/<unknown id> serves the not-found page`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let response = get client $"{server.BaseUrl}/s/doesnotexist"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Contains("not found", body response)

[<Fact>]
let ``POST /s/<invalid id> serves the not-found page`` () =
    use server = new TestServer(Map.empty)
    use client = newClient ()

    let response = post client $"{server.BaseUrl}/s/ab" "rss=example.com/x"

    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Contains("not found", body response)
