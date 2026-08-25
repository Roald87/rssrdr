module SimpleRssServer.Tests.HtmlRendererTests

open System
open System.Text
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
let ``Test landing page displays correct version number`` () =
    Assert.Contains($"v{versionNumber}", string landingPage)

[<Fact>]
let ``Test configPage prefills textarea with valid URIs`` () =
    let validUri1 = Uri "https://example.com/feed1"
    let validUri2 = Uri "http://example.com/feed2"

    let rssUrls =
        [ Ok validUri1
          Ok validUri2
          Error(HostNameMustContainDot(InvalidUri.Create "invalid-uri")) ]

    let resultHtml = configPage rssUrls None |> string

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

[<Fact>]
let ``chronologicalFeedsPage with empty query shows config link without query string`` () =
    let result = chronologicalFeedsPage (Query.Create "") [] |> string

    Assert.Contains("""href="/config.html/">rssrdr""", result)

[<Fact>]
let ``configPage contains save collection checkbox`` () =
    let html = configPage [] None |> string

    Assert.Contains("saveCollection", html)
    Assert.Contains("Save feed collection", html)

[<Fact>]
let ``configPage save collection checkbox is unchecked by default`` () =
    let html = configPage [] None |> string

    Assert.DoesNotContain("id=\"saveCollection\" checked", html)

[<Fact>]
let ``configPage save collection checkbox is checked when editing an existing collection`` () =
    let html = configPage [] (Some(CollectionId "abc123de456")) |> string

    Assert.Contains("""id="saveCollection" checked""", html)
    Assert.Contains("""id="existingCode" value="abc123de456""", html)

[<Fact>]
let ``collectionNotFoundPage shows collection id`` () =
    let html = collectionNotFoundPage (CollectionId "abc123") |> string

    Assert.Contains("abc123", html)

[<Fact>]
let ``collectionFeedsPageShell has config and shuffle links for the collection id`` () =
    let html = collectionFeedsPageShell (CollectionId "mycode1") |> string

    Assert.Contains("""href="/config.html?s=mycode1">config/""", html)
    Assert.Contains("""href="/s/mycode1/shuffle" style="margin-left: 20px;">shuffle/""", html)

[<Fact>]
let ``collectionShuffledPageShell has config and chronological links for the collection id`` () =
    let html = collectionShuffledPageShell (CollectionId "mycode1") |> string

    Assert.Contains("""href="/config.html?s=mycode1">config/""", html)
    Assert.Contains("""href="/s/mycode1" style="margin-left: 20px;">chronological/""", html)
