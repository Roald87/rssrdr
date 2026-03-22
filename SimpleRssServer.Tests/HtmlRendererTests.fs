module SimpleRssServer.Tests.HtmlRendererTests

open System
open System.Linq
open System.Text
open System.Xml.Linq
open Xunit

open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.HtmlRenderer
open SimpleRssServer.RssParser

[<Fact>]
let ``Test convertArticleToHtml encodes special characters`` () =
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
let ``removeFromQuery removes a matching feed from a multi-feed query`` () =
    let result =
        removeFromQuery (Query.Create "?rss=example.com/feed&rss=other.com/feed") "https://example.com/feed"

    Assert.Equal("?rss=other.com/feed", result)

[<Fact>]
let ``removeFromQuery returns slash when removing the last feed`` () =
    let result =
        removeFromQuery (Query.Create "?rss=example.com/feed") "https://example.com/feed"

    Assert.Equal("/", result)

[<Fact>]
let ``removeFromQuery handles http prefix in query param`` () =
    let result =
        removeFromQuery (Query.Create "?rss=http://example.com/feed&rss=other.com/feed") "http://example.com/feed"

    Assert.Equal("?rss=other.com/feed", result)

[<Fact>]
let ``removeFromQuery leaves query unchanged if feedUrl not found`` () =
    let result =
        removeFromQuery (Query.Create "?rss=other.com/feed") "https://example.com/feed"

    Assert.Equal("?rss=other.com/feed", result)

[<Fact>]
let ``removeFromQuery removes only the specified feed when two feeds share the same base url`` () =
    let result =
        removeFromQuery (Query.Create "?rss=example.com/feed1&rss=example.com/feed2") "https://example.com/feed1"

    Assert.Equal("?rss=example.com/feed2", result)

[<Fact>]
let ``Test landing page displays correct version number using XML parser`` () =
    let fsprojContent =
        System.IO.File.ReadAllText "../../../../SimpleRssServer/SimpleRssServer.fsproj"

    let xmlDoc = XDocument.Parse fsprojContent
    let version = xmlDoc.Descendants(XName.Get "Version").FirstOrDefault().Value

    Assert.Contains($"v{version}", string landingPage)

[<Fact>]
let ``Test configPage handles valid and invalid URIs`` () =
    let validUri1 = Uri "https://example.com/feed1"
    let validUri2 = Uri "http://example.com/feed2"
    let invalidUri1 = InvalidUri.Create "invalid-uri"
    let invalidUri2 = InvalidUri.Create "not-a-url"

    let rssUrls =
        [| Ok validUri1
           Ok validUri2
           Error(HostNameMustContainDot invalidUri1)
           Error(HostNameMustContainDot invalidUri2) |]

    let resultHtml = configPage rssUrls |> string

    // Use regex to extract the value of the textarea with id 'feeds'
    let textareaValue =
        let m =
            RegularExpressions.Regex.Match(
                resultHtml,
                "<textarea id='feeds'[^>]*>(.*?)</textarea>",
                RegularExpressions.RegexOptions.Singleline
            )

        if m.Success then
            m.Groups.[1].Value
        else
            failwith "Textarea not found"

    let expectedValidUris = "example.com/feed1\nhttp://example.com/feed2"
    Assert.Equal(expectedValidUris, textareaValue)

    // Check both invalid URIs in invalid-uris div
    Assert.Contains(invalidUri1.Value, resultHtml)
    Assert.Contains(invalidUri2.Value, resultHtml)
    Assert.Contains("<div class='invalid-uris'>", resultHtml)

[<Fact>]
let ``Test feedDiscoveryPage renders confirmed feeds in textarea and checkboxes for toSelect`` () =
    let confirmedUris =
        [| Uri "https://example.com/feed1"; Uri "http://example.com/feed2" |]

    let toSelect =
        [ { Title = "RSS Feed"
            Url = "https://site.com/rss.xml" }
          { Title = "Atom Feed"
            Url = "https://site.com/atom.xml" } ]

    let resultHtml = feedDiscoveryPage confirmedUris toSelect |> string

    let textareaValue =
        let m =
            RegularExpressions.Regex.Match(
                resultHtml,
                "<textarea id='feeds'[^>]*>(.*?)</textarea>",
                RegularExpressions.RegexOptions.Singleline
            )

        if m.Success then
            m.Groups.[1].Value
        else
            failwith "Textarea not found in feedDiscoveryPage"

    Assert.Contains("example.com/feed1", textareaValue)
    Assert.Contains("http://example.com/feed2", textareaValue)

    Assert.Contains("value='https://site.com/rss.xml'", resultHtml)
    Assert.Contains("RSS Feed", resultHtml)
    Assert.Contains("value='https://site.com/atom.xml'", resultHtml)
    Assert.Contains("Atom Feed", resultHtml)
    Assert.Contains("site.com", resultHtml)
