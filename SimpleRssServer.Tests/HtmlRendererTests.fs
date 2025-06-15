module SimpleRssServer.Tests.HtmlRendererTests

open System
open System.Linq
open System.Xml.Linq
open Xunit

open SimpleRssServer.HtmlRenderer
open SimpleRssServer.RssParser

[<Fact>]
let ``Test convertArticleToHtml encodes special characters`` () =
    let expected =
        """
    <div class="feed-item">
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

    Assert.Equal(expected, actual)

[<Fact>]
let ``Test landing page displays correct version number using XML parser`` () =
    let fsprojContent =
        System.IO.File.ReadAllText "../../../../SimpleRssServer/SimpleRssServer.fsproj"

    let xmlDoc = XDocument.Parse fsprojContent
    let version = xmlDoc.Descendants(XName.Get "Version").FirstOrDefault().Value

    Assert.Contains($"v{version}", landingPage)
