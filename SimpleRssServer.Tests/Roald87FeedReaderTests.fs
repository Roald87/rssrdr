module SimpleRssServer.Tests.Roald87FeedReaderTests

open System

open Roald87.FeedReader
open Xunit

[<Fact>]
let ``test Roald87.FeedReader with roaldinch`` () =
    let feed = FeedReader.ReadFromFile("data/roaldinch.xml")

    Assert.Equal(10, feed.Items.Count)
    Assert.Equal("Groepsreserveringen", feed.Items[0].Title)
    Assert.Equal(DateTime(2024, 8, 6, 0, 0, 0) |> Nullable, feed.Items[0].PublishingDate)
    Assert.Equal("https://roaldin.ch/groepsreserveringen", feed.Items[0].Link)

    Assert.Equal(
        "Regelmatig zie ik hier treincoupés die zijn gereserveerd voor een groep. Vaak zijn dit schoolklassen op een uitje, maar soms ook andere groepen. Zo had ik laatst een wandeling met collega’s, waarvoor een gedeelte van de coupé was gereserveerd. In Nederland",
        feed.Items.[0].Description.Substring(0, 256)
    )

[<Fact>]
let ``test Roald87.FeedReader with zoesklot`` () =
    let feed = FeedReader.ReadFromFile("data/zoesklot.xml")

    Assert.Equal(5, feed.Items.Count)
    Assert.Equal("Duitse shag", feed.Items[0].Title)
    Assert.Equal("https://www.zoesklot.nl/duitse-shag/", feed.Items[0].Link)
    Assert.Equal(DateTime(2024, 8, 6, 13, 26, 32) |> Nullable, feed.Items[0].PublishingDate)

    Assert.Equal(
        "Bij de kassa van de Jumbo ziet M. mij en ik kan niet meer vluchten naar een andere kassa. M., een magere vijftiger met donker achterovergekamd golvend haar; enkele tattoos, oorbel en kunstgebitje ken ik van de volkstuin. Als we oogcontact hebben dan word ik meegezogen in zijn persoonlijke wereld. “Het was zo druk man"
            .Substring(0, 255),
        feed.Items[0].Description.Substring(0, 255)
    )

[<Fact>]
let ``test Roald87.FeedReader with spectrum`` () =
    // https://spectrum.ieee.org/feeds/feed.rss
    let feed = FeedReader.ReadFromFile("data/spectrum.rss")

    Assert.Equal(30, feed.Items.Count)
    Assert.Equal("Quantum Cryptography Has Everyone Scrambling", feed.Items[0].Title)

[<Fact>]
let ``test Roald87.FeedReader with quanta`` () =
    // https://www.quantamagazine.org/feed/
    let feed = FeedReader.ReadFromFile("data/quanta.xml")

    Assert.Equal(5, feed.Items.Count)
    Assert.Equal("Physicists Pinpoint the Quantum Origin of the Greenhouse Effect", feed.Items[0].Title)


[<Fact>]
let ``test Roald87.FeedReader with nature content`` () =
    // http://feeds.nature.com/nature/rss/current
    let feed = FeedReader.ReadFromFile("data/nature.rss")

    Assert.Equal(75, feed.Items.Count)

    Assert.Equal(
        """<p>Nature, Published online: 20 August 2024; <a href="https://www.nature.com/articles/s41586-024-07956-2">doi:10.1038/s41586-024-07956-2</a></p>Author Correction: Anti-TIGIT antibody improves PD-L1 blockade through myeloid and T<sub>reg</sub> cells""",
        feed.Items[0].Content
    )

[<Fact>]
let ``test Roald87.FeedReader with wikenigma content`` () =
    // https://wikenigma.org.uk/feed.php
    let feed = FeedReader.ReadFromFile("data/wikenigma.xml")

    Assert.Equal(10, feed.Items.Count)

    Assert.Equal("'Penguin' etymology - external edit", feed.Items[0].Title)
    // 2024-03-01T13:26:09+00:00
    Assert.Equal(DateTime(2024, 3, 1, 13, 26, 9) |> Nullable, feed.Items[0].PublishingDate)
