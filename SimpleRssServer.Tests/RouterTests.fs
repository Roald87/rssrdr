module SimpleRssServer.Tests.RouterTests

open Xunit

open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.Router

[<Fact>]
let ``config.html routes to ConfigPage`` () =
    Assert.Equal(ConfigPage, parseRoute "GET" "/config.html")

[<Fact>]
let ``config.html with a collection query routes to ConfigPage`` () =
    Assert.Equal(ConfigPage, parseRoute "GET" "/config.html?s=abc123")

[<Fact>]
let ``config.html with a feed query routes to ConfigPage`` () =
    Assert.Equal(ConfigPage, parseRoute "GET" "/config.html/?rss=example.com/feed")

[<Fact>]
let ``root with rss query routes to ChronologicalFeeds`` () =
    Assert.Equal(ChronologicalFeeds, parseRoute "GET" "/?rss=example.com/feed")

[<Fact>]
let ``shuffle with rss query routes to ShuffleFeeds`` () =
    Assert.Equal(ShuffleFeeds, parseRoute "GET" "/shuffle?rss=example.com/feed")

[<Fact>]
let ``robots.txt routes to RobotsTxt`` () =
    Assert.Equal(RobotsTxt, parseRoute "GET" "/robots.txt")

[<Fact>]
let ``sitemap.xml routes to SitemapXml`` () =
    Assert.Equal(SitemapXml, parseRoute "GET" "/sitemap.xml")

[<Fact>]
let ``apple-touch-icon.png routes to AppleTouchIcon`` () =
    Assert.Equal(AppleTouchIcon, parseRoute "GET" "/apple-touch-icon.png")

[<Fact>]
let ``POST /s routes to CreateCollection`` () =
    Assert.Equal(CreateCollection, parseRoute "POST" "/s")

[<Fact>]
let ``GET /s routes to LandingPage`` () =
    Assert.Equal(LandingPage, parseRoute "GET" "/s")

[<Fact>]
let ``GET a collection path routes to ViewCollection`` () =
    Assert.Equal(ViewCollection(CollectionId "abc123"), parseRoute "GET" "/s/abc123")

[<Fact>]
let ``GET a collection path with a query strips the query from the id`` () =
    Assert.Equal(ViewCollection(CollectionId "abc123"), parseRoute "GET" "/s/abc123?foo=bar")

[<Fact>]
let ``GET a collection shuffle path routes to ViewCollectionShuffle, not ViewCollection`` () =
    Assert.Equal(ViewCollectionShuffle(CollectionId "abc123"), parseRoute "GET" "/s/abc123/shuffle")

[<Fact>]
let ``GET a collection shuffle path with a query strips the query from the id`` () =
    Assert.Equal(ViewCollectionShuffle(CollectionId "abc123"), parseRoute "GET" "/s/abc123/shuffle?foo=bar")

[<Fact>]
let ``POST a collection path routes to UpdateCollection`` () =
    Assert.Equal(UpdateCollection(CollectionId "abc123"), parseRoute "POST" "/s/abc123")

[<Fact>]
let ``POST a collection shuffle path falls through to UpdateCollection with the raw id`` () =
    // CollectionShuffleId only matches GET, so POST is handled by the collection-id arm.
    Assert.Equal(UpdateCollection(CollectionId "abc123/shuffle"), parseRoute "POST" "/s/abc123/shuffle")

[<Fact>]
let ``DELETE a collection path routes to LandingPage`` () =
    Assert.Equal(LandingPage, parseRoute "DELETE" "/s/abc123")

[<Fact>]
let ``an id with invalid characters still routes to ViewCollection`` () =
    // Validation happens in the handler, not the router.
    Assert.Equal(ViewCollection(CollectionId "../etc/passwd"), parseRoute "GET" "/s/../etc/passwd")

[<Fact>]
let ``root without a query routes to LandingPage`` () =
    Assert.Equal(LandingPage, parseRoute "GET" "/")

[<Fact>]
let ``root with a non-rss query routes to LandingPage`` () =
    Assert.Equal(LandingPage, parseRoute "GET" "/?foo=bar")

[<Fact>]
let ``bare shuffle without an rss query routes to LandingPage`` () =
    Assert.Equal(LandingPage, parseRoute "GET" "/shuffle")

[<Fact>]
let ``an unknown path routes to LandingPage`` () =
    Assert.Equal(LandingPage, parseRoute "GET" "/nonsense")
