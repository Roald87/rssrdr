module SimpleRssServer.Tests.HtmlRendererTests

open System
open System.Linq
open System.Text
open System.Xml.Linq
open Xunit

open SimpleRssServer.HtmlRenderer
open SimpleRssServer.RssParser

[<Fact>]
let ``Test convertArticleToHtml encodes special characters`` () =
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

    // Comparing strings, instead of Html, is easier to see where they differ
    Assert.Equal(expected, actual)

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
    let invalidUri1 = "invalid-uri"
    let invalidUri2 = "not-a-url"

    let rssUrls = [| Ok validUri1; Ok validUri2; Error invalidUri1; Error invalidUri2 |]

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
    Assert.Contains(invalidUri1, resultHtml)
    Assert.Contains(invalidUri2, resultHtml)
    Assert.Contains("<div class='invalid-uris'>", resultHtml)
