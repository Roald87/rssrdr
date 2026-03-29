module SimpleRssServer.Tests.HtmlRendererTests

open System
open System.Linq
open System.Text
open System.Xml.Linq
open Xunit

open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.HtmlRenderer

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
let ``Test configPage prefills textarea with valid URIs`` () =
    let validUri1 = Uri "https://example.com/feed1"
    let validUri2 = Uri "http://example.com/feed2"

    let rssUrls =
        [| Ok validUri1
           Ok validUri2
           Error(HostNameMustContainDot(InvalidUri.Create "invalid-uri")) |]

    let resultHtml = configPage rssUrls |> string

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

    Assert.Equal("example.com/feed1\nhttp://example.com/feed2", textareaValue)
