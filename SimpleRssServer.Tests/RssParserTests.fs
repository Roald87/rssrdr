module SimpleRssServer.Tests.RssParser

open Microsoft.Extensions.Logging.Abstractions
open System
open System.IO
open System.Net
open Xunit

open SimpleRssServer.Config
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.RssParser
open SimpleRssServer.Tests.TestHelpers

[<Fact>]
let ``createErrorArticle sets ArticleUrl from InvalidUriHostname`` () =
    let rawUri = "not-a-valid-uri"
    let article = createErrorArticle (InvalidUriHostname(InvalidUri.Create rawUri))
    Assert.Equal(rawUri, article.ArticleUrl)

[<Fact>]
let ``createErrorArticle sets ArticleUrl from PreviousHttpRequestFailed`` () =
    let uri = Uri "https://example.com/feed"

    let article =
        createErrorArticle (PreviousHttpRequestFailed(uri, TimeSpan.FromHours 1.0))

    Assert.Equal(uri.AbsoluteUri, article.ArticleUrl)

[<Fact>]
let ``tryParseFeed returns InvalidRssFeedFormat for non-RSS content`` () =
    let uri = Uri "https://example.com"
    let html = "<html><head><title>Not RSS</title></head><body>Test</body></html>"
    let result = tryParseFeed NullLogger.Instance html uri

    match result with
    | Error(InvalidRssFeedFormat(errorUri, _)) -> Assert.Equal(uri, errorUri)
    | _ -> Assert.Fail "Expected Error(InvalidRssFeedFormat ...)"

[<Fact>]
let ``tryParseFeed returns Ok Feed for valid RSS content`` () =
    let uri = Uri "https://roaldin.ch/feed.xml"
    let content = File.ReadAllText "data/roaldinch.xml"
    let result = tryParseFeed NullLogger.Instance content uri
    Assert.True(Result.isOk result)

let articlesFromFile pathToXml url =
    File.ReadAllText pathToXml
    |> (fun x -> tryParseFeed NullLogger.Instance x (Uri url))
    |> fun x ->
        match x with
        | Ok feed -> feed
        | Error err -> failwithf $"Failed to parse xml to Feed, {err}"
    |> toArticles

[<Fact>]
let ``Test parseRss with roaldinch.xml`` () =
    let feedUrl = "https://roaldin.ch/feed"

    let result = articlesFromFile "data/roaldinch.xml" feedUrl

    let expectedFirst =
        { PostDate = Some(DateTime(2024, 8, 6, 0, 0, 0))
          Title = "Groepsreserveringen"
          ArticleUrl = "https://roaldin.ch/groepsreserveringen"
          FeedUrl = feedUrl
          Text =
            "Regelmatig zie ik hier treincoupés die zijn gereserveerd voor een groep. Vaak zijn dit schoolklassen op een uitje, maar soms ook andere groepen. Zo had ik laatst een wandeling met collega’s, waarvoor een gedeelte van de coupé was gereserveerd. In Nederlan..." }

    let expectedLast =
        { PostDate = Some(DateTime(2024, 6, 16, 0, 0, 0))
          Title = "Promoveren"
          ArticleUrl = "https://roaldin.ch/promoveren"
          FeedUrl = feedUrl
          Text =
            "Recent had ik het genoegen om mijn eerste Zwitserse verdediging bij te wonen van een promovendus. Het viel me tegen hoe dit gevierd werd. Zelf ben ik in Groningen gepromoveerd en daar waren verschillende tradities en gewoonten. Zo kiest de promovendus twee paranimfen, een soort getuigen zoals bij huwelijken, die de promovendus helpen met allerlei zaken rondom de verdediging."
                .Substring(0, ArticleDescriptionLength)
            + "..." }

    Assert.Equal(10, result.Length)
    Assert.Equal(expectedFirst, result[0])
    Assert.Equal(expectedLast, result[result.Length - 1])

[<Fact>]
let ``Test parseRss with nature.rss that doesn't contain time information`` () =
    let result = articlesFromFile "data/nature.rss" "https://example.com"

    let expectedFirst =
        { PostDate = Some(DateTime(2024, 8, 19))
          Title =
            "Author Correction: Anti-TIGIT antibody improves PD-L1 blockade through myeloid and T<sub>reg</sub> cells"
          ArticleUrl = "https://www.nature.com/articles/s41586-024-07956-2"
          FeedUrl = "https://example.com/"
          Text =
            "Nature, Published online: 20 August 2024; doi:10.1038/s41586-024-07956-2Author Correction: Anti-TIGIT antibody improves PD-L1 blockade through myeloid and Treg cells" }

    let expectedLast =
        { PostDate = Some(DateTime(2024, 8, 13))
          Title = "Stonehenge’s enigmatic centre stone was hauled 800 kilometres from Scotland"
          ArticleUrl = "https://www.nature.com/articles/d41586-024-02584-2"
          FeedUrl = "https://example.com/"
          Text =
            "Nature, Published online: 14 August 2024; doi:10.1038/d41586-024-02584-2By assessing the age of ancient crystals, researchers have traced the monument’s greenish Altar Stone to a northern rock basin." }

    Assert.Equal(75, result.Length)

    Assert.Equal(expectedFirst.Title, result[0].Title)
    Assert.Equal(expectedFirst.Text, result[0].Text)
    Assert.Equal(expectedFirst.ArticleUrl, result[0].ArticleUrl)
    Assert.True((expectedFirst.PostDate.Value - result[0].PostDate.Value).TotalDays < 1)

    Assert.Equal(expectedLast.Title, result[result.Length - 1].Title)
    Assert.Equal(expectedLast.Text, result[result.Length - 1].Text)
    Assert.Equal(expectedLast.ArticleUrl, result[result.Length - 1].ArticleUrl)

    Assert.True((expectedLast.PostDate.Value - result[result.Length - 1].PostDate.Value).TotalDays < 1)

[<Fact>]
let ``Test parsing date if only update date is available`` () =
    let result = articlesFromFile "data/rachel.xml" "https://example.com"

    let expectedFirst = Some(DateTime(2024, 8, 18, 23, 16, 27))
    Assert.True result[0].PostDate.IsSome
    Assert.True((expectedFirst.Value - result[0].PostDate.Value).TotalSeconds < 1.0)

    let expectedLast = Some(DateTime(2023, 2, 24, 8, 45, 28))
    Assert.True((expectedLast.Value - result[result.Length - 1].PostDate.Value).TotalSeconds < 1.0)

[<Fact>]
let ``Test get content for article text if description is empty`` () =
    let result = articlesFromFile "data/rachel.xml" "https://example.com"

    let expectedText =
        "Yeah, it's another thing about feed readers. I don't blame you if you want to skip this one. A reader (that is, a person!) reached out earlier and asked me to look at a bug report for a feed reader. It seems they passed along some of the details from one of my ear"
            .Substring(0, ArticleDescriptionLength)
        + "..."

    Assert.Equal(expectedText, result[0].Text)

// ---------------------------------------------------------------------------
// stripHtml
// ---------------------------------------------------------------------------

[<Fact>]
let ``stripHtml removes html tags`` () =
    Assert.Equal("Hello world", stripHtml "<p>Hello <strong>world</strong></p>")

[<Fact>]
let ``stripHtml removes tags with attributes and nesting`` () =
    Assert.Equal("link", stripHtml """<div class="a"><a href="http://x">link</a></div>""")

[<Fact>]
let ``stripHtml collapses whitespace runs into a single space`` () =
    Assert.Equal("a b", stripHtml "a  \n\t  b")

[<Fact>]
let ``stripHtml trims leading and trailing whitespace`` () =
    Assert.Equal("text", stripHtml "   text   ")

[<Fact>]
let ``stripHtml returns empty string for null`` () = Assert.Equal("", stripHtml null)

[<Fact>]
let ``stripHtml returns empty string for whitespace-only input`` () = Assert.Equal("", stripHtml "   \n\t ")

[<Fact>]
let ``stripHtml leaves tag-free text unchanged`` () =
    Assert.Equal("Just plain text.", stripHtml "Just plain text.")

// ---------------------------------------------------------------------------
// Shared helpers for the UriProcessState pipeline steps
// ---------------------------------------------------------------------------

let private notRssHtml =
    "<html><head><title>Not RSS</title></head><body>Test</body></html>"

let private feedLinkHtml (href: string) =
    $"""<html><head><link rel="alternate" type="application/rss+xml" title="Feed" href="{href}"></head><body></body></html>"""

let private feedFrom (url: string) (count: int) =
    DummyXmlFeedFactory.create url count
    |> fun xml -> tryParseFeed NullLogger.Instance xml (Uri url)
    |> getOk

let private dummyArticle title =
    { PostDate = None
      Title = title
      ArticleUrl = "https://example.com/a"
      FeedUrl = "https://example.com/feed"
      Text = "text" }

// ---------------------------------------------------------------------------
// feedToArticles
// ---------------------------------------------------------------------------

[<Fact>]
let ``feedToArticles turns ParsedLiveFeed into FeedArticles`` () =
    let feed = feedFrom "https://example.com/feed" 3

    match feedToArticles (ParsedLiveFeed(UnparsedXml "xml", feed)) with
    | FeedArticles articles -> Assert.Equal(3, articles.Length)
    | x -> Assert.Fail $"Expected FeedArticles, got {x}"

[<Fact>]
let ``feedToArticles turns ParsedCachedFeed into FeedArticles`` () =
    let feed = feedFrom "https://example.com/feed" 2

    match feedToArticles (ParsedCachedFeed feed) with
    | FeedArticles articles -> Assert.Equal(2, articles.Length)
    | x -> Assert.Fail $"Expected FeedArticles, got {x}"

[<Fact>]
let ``feedToArticles appends an error article for ParsedStaleFeed`` () =
    let feed = feedFrom "https://example.com/feed" 2

    let err =
        PreviousHttpRequestFailed(Uri "https://example.com/feed", TimeSpan.FromHours 1.0)

    match feedToArticles (ParsedStaleFeed(feed, err)) with
    | DegradedArticles articles ->
        Assert.Equal(3, articles.Length)
        Assert.Equal("Error", (List.last articles).Title)
    | x -> Assert.Fail $"Expected DegradedArticles, got {x}"

[<Fact>]
let ``feedToArticles turns ProcessingError into a single error article`` () =
    match feedToArticles (ProcessingError(NoRssFeedsFoundInPage(Uri "https://example.com"))) with
    | DegradedArticles [ article ] -> Assert.Equal("Error", article.Title)
    | x -> Assert.Fail $"Expected DegradedArticles with one article, got {x}"

[<Fact>]
let ``feedToArticles passes other states through unchanged`` () =
    let state = TryFetchFromCache(Uri "https://example.com/feed")
    Assert.Equal(state, feedToArticles state)

// ---------------------------------------------------------------------------
// parseFeedResult
// ---------------------------------------------------------------------------

[<Fact>]
let ``parseFeedResult parses a live http response into ParsedLiveFeed`` () =
    let xml = DummyXmlFeedFactory.create "https://example.com/feed" 3
    let uri = Uri "https://example.com/feed"

    match parseFeedResult NullLogger.Instance (UnparsedHttpResponse(xml, uri)) with
    | ParsedLiveFeed(UnparsedXml raw, _) -> Assert.Equal(xml, raw)
    | x -> Assert.Fail $"Expected ParsedLiveFeed, got {x}"

[<Fact>]
let ``parseFeedResult marks a non-feed http response as NotRssContent`` () =
    let uri = Uri "https://example.com"

    match parseFeedResult NullLogger.Instance (UnparsedHttpResponse(notRssHtml, uri)) with
    | NotRssContent(raw, originalUri) ->
        Assert.Equal(notRssHtml, raw)
        Assert.Equal(uri, originalUri)
    | x -> Assert.Fail $"Expected NotRssContent, got {x}"

[<Fact>]
let ``parseFeedResult parses cached content into ParsedCachedFeed`` () =
    let xml = DummyXmlFeedFactory.create "https://example.com/feed" 4
    let uri = Uri "https://example.com/feed"

    match parseFeedResult NullLogger.Instance (UnparsedCachedContent(xml, uri)) with
    | ParsedCachedFeed feed -> Assert.Equal(4, feed.Items.Count)
    | x -> Assert.Fail $"Expected ParsedCachedFeed, got {x}"

[<Fact>]
let ``parseFeedResult turns unparsable cached content into ProcessingError`` () =
    let uri = Uri "https://example.com"

    match parseFeedResult NullLogger.Instance (UnparsedCachedContent(notRssHtml, uri)) with
    | ProcessingError(InvalidRssFeedFormat(errUri, _)) -> Assert.Equal(uri, errUri)
    | x -> Assert.Fail $"Expected ProcessingError(InvalidRssFeedFormat ...), got {x}"

[<Fact>]
let ``parseFeedResult keeps the original error for stale cached content that parses`` () =
    let xml = DummyXmlFeedFactory.create "https://example.com/feed" 2
    let uri = Uri "https://example.com/feed"
    let origErr = PreviousHttpRequestFailed(uri, TimeSpan.FromHours 2.0)

    match parseFeedResult NullLogger.Instance (UnparsedStaleCachedContent(xml, uri, origErr)) with
    | ParsedStaleFeed(feed, err) ->
        Assert.Equal(2, feed.Items.Count)
        Assert.Equal(origErr, err)
    | x -> Assert.Fail $"Expected ParsedStaleFeed, got {x}"

[<Fact>]
let ``parseFeedResult falls back to the original error for stale cached content that fails to parse`` () =
    let uri = Uri "https://example.com"
    let origErr = PreviousHttpRequestFailed(uri, TimeSpan.FromHours 2.0)

    match parseFeedResult NullLogger.Instance (UnparsedStaleCachedContent(notRssHtml, uri, origErr)) with
    | ProcessingError err -> Assert.Equal(origErr, err)
    | x -> Assert.Fail $"Expected ProcessingError, got {x}"

[<Fact>]
let ``parseFeedResult passes other states through unchanged`` () =
    let state = TryFetchFromCache(Uri "https://example.com/feed")
    Assert.Equal(state, parseFeedResult NullLogger.Instance state)

// ---------------------------------------------------------------------------
// checkIfDiscoveryFeeds
// ---------------------------------------------------------------------------

[<Fact>]
let ``checkIfDiscoveryFeeds resolves an absolute feed link to TryFetchFromCache`` () =
    let pageUri = Uri "https://example.com/blog/post"
    let feedUrl = "https://example.com/feed.xml"

    match checkIfDiscoveryFeeds (NotRssContent(feedLinkHtml feedUrl, pageUri)) with
    | [ TryFetchFromCache uri ] -> Assert.Equal(feedUrl, uri.AbsoluteUri)
    | x -> Assert.Fail $"Expected a single TryFetchFromCache, got {x}"

[<Fact>]
let ``checkIfDiscoveryFeeds resolves a relative feed link against the page url`` () =
    let pageUri = Uri "https://example.com/blog/post"

    match checkIfDiscoveryFeeds (NotRssContent(feedLinkHtml "/feed.xml", pageUri)) with
    | [ TryFetchFromCache uri ] -> Assert.Equal("https://example.com/feed.xml", uri.AbsoluteUri)
    | x -> Assert.Fail $"Expected a single TryFetchFromCache, got {x}"

[<Fact>]
let ``checkIfDiscoveryFeeds reports NoRssFeedsFoundInPage when the page has no feed links`` () =
    let pageUri = Uri "https://example.com"

    match checkIfDiscoveryFeeds (NotRssContent(notRssHtml, pageUri)) with
    | [ ProcessingError(NoRssFeedsFoundInPage uri) ] -> Assert.Equal(pageUri, uri)
    | x -> Assert.Fail $"Expected NoRssFeedsFoundInPage, got {x}"

[<Fact>]
let ``checkIfDiscoveryFeeds passes other states through as a singleton list`` () =
    let state = TryFetchFromCache(Uri "https://example.com/feed")

    match checkIfDiscoveryFeeds state with
    | [ passed ] -> Assert.Equal(state, passed)
    | x -> Assert.Fail $"Expected a singleton list, got {x}"

// ---------------------------------------------------------------------------
// onlyFeedArticles
// ---------------------------------------------------------------------------

[<Fact>]
let ``onlyFeedArticles returns the articles from FeedArticles`` () =
    let articles = [ dummyArticle "a"; dummyArticle "b" ]
    Assert.Equal<Article list>(articles, onlyFeedArticles (FeedArticles articles))

[<Fact>]
let ``onlyFeedArticles returns the articles from DegradedArticles`` () =
    let articles = [ dummyArticle "a" ]
    Assert.Equal<Article list>(articles, onlyFeedArticles (DegradedArticles articles))

[<Fact>]
let ``onlyFeedArticles returns an empty list for other states`` () =
    Assert.Empty(onlyFeedArticles (TryFetchFromCache(Uri "https://example.com/feed")))

// ---------------------------------------------------------------------------
// createErrorArticle — remaining DomainError variants
// ---------------------------------------------------------------------------

[<Fact>]
let ``createErrorArticle sets links from InvalidUriFormat`` () =
    let raw = "http://no dot"

    let article =
        createErrorArticle (InvalidUriFormat(InvalidUri.Create raw, Exception "bad"))

    Assert.Equal("Error", article.Title)
    Assert.Equal(raw, article.ArticleUrl)
    Assert.Equal(raw, article.FeedUrl)

[<Fact>]
let ``createErrorArticle mentions the saved version for PreviousHttpRequestFailedButPageCached`` () =
    let uri = Uri "https://example.com/feed"

    let article =
        createErrorArticle (PreviousHttpRequestFailedButPageCached(uri, TimeSpan.FromHours 4.0))

    Assert.Equal(uri.AbsoluteUri, article.ArticleUrl)
    Assert.Contains("saved version", article.Text)

[<Fact>]
let ``createErrorArticle mentions the timeout for HttpRequestTimedOut`` () =
    let uri = Uri "https://example.com/feed"

    let article =
        createErrorArticle (HttpRequestTimedOut(uri, TimeSpan.FromSeconds 5.0))

    Assert.Equal(uri.AbsoluteUri, article.ArticleUrl)
    Assert.Contains("timed out", article.Text)

[<Fact>]
let ``createErrorArticle mentions the host for HttpException`` () =
    let article =
        createErrorArticle (HttpException(Uri "https://example.com/feed", Exception "boom"))

    Assert.Equal("Error", article.Title)
    Assert.Contains("example.com", article.Text)

[<Fact>]
let ``createErrorArticle mentions the status for HttpRequestNonSuccessStatus`` () =
    let uri = Uri "https://example.com/feed"

    let article =
        createErrorArticle (HttpRequestNonSuccessStatus(uri, HttpStatusCode.NotFound))

    Assert.Equal(uri.AbsoluteUri, article.ArticleUrl)
    Assert.Contains("NotFound", article.Text)

[<Fact>]
let ``createErrorArticle sets links from InvalidRssFeedFormat`` () =
    let uri = Uri "https://example.com/feed"
    let article = createErrorArticle (InvalidRssFeedFormat(uri, Exception "nope"))
    Assert.Equal("Error", article.Title)
    Assert.Equal(uri.AbsoluteUri, article.FeedUrl)
    Assert.Equal(uri.AbsoluteUri, article.ArticleUrl)

[<Fact>]
let ``createErrorArticle reports no feeds found and sets a post date for NoRssFeedsFoundInPage`` () =
    let article = createErrorArticle (NoRssFeedsFoundInPage(Uri "https://example.com"))
    Assert.Contains("No RSS feeds", article.Text)
    Assert.True(article.PostDate.IsSome)
