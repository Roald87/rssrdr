module SimpleRssServer.Tests.RssParser

open Microsoft.Extensions.Logging.Abstractions
open System
open System.IO
open Xunit

open SimpleRssServer.Config
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.RssParser

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

let makeTempCacheConfig () =
    { Dir = OsPath(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()))
      Expiration = TimeSpan.FromHours 1.0 }

// [<Fact>]
// let ``parseFeedResult with invalid FreshContent returns Error and does not write cache`` () =
//     let uri = Uri "https://example.com"
//     let cacheConfig = makeTempCacheConfig ()
//     Directory.CreateDirectory cacheConfig.Dir

//     let result =
//         parseFeedResult NullLogger.Instance cacheConfig (FreshContent("<html>not rss</html>", uri))

//     match result with
//     | Error articles ->
//         Assert.Single articles |> ignore
//         Assert.Equal("Error", (List.head articles).Title)
//         let cachePath = Path.Combine(cacheConfig.Dir, convertUrlToValidFilename uri)
//         Assert.False(File.Exists cachePath, "Expected no cache file to be written")
//     | Ok _ -> Assert.Fail "Expected Error"

//     Directory.DeleteRecursive cacheConfig.Dir
