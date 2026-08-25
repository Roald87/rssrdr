module SimpleRssServer.Tests.CollectionsTests

open System
open Xunit

open SimpleRssServer.Collections
open SimpleRssServer.DomainPrimitiveTypes
open TestHelpers

[<Fact>]
let ``generateCollectionId produces 11-character base64url string`` () =
    let (CollectionId collectionId) = generateCollectionId ()
    Assert.Equal(11, collectionId.Length)

    Assert.True(
        collectionId
        |> Seq.forall (fun c -> Char.IsLetterOrDigit c || c = '-' || c = '_')
    )

[<Fact>]
let ``isValidCollectionId accepts valid ids`` () =
    Assert.True(isValidCollectionId (CollectionId "aBcDeFgHiJk"))
    Assert.True(isValidCollectionId (CollectionId "abc123"))
    Assert.True(isValidCollectionId (CollectionId "abc-def_123"))

[<Fact>]
let ``isValidCollectionId rejects empty string`` () =
    Assert.False(isValidCollectionId (CollectionId ""))

[<Fact>]
let ``isValidCollectionId rejects path traversal and special characters`` () =
    Assert.False(isValidCollectionId (CollectionId "../etc/passwd"))
    Assert.False(isValidCollectionId (CollectionId "abc/def"))
    Assert.False(isValidCollectionId (CollectionId "abc?edit"))

[<Fact>]
let ``save and tryLoad round-trip a feed list`` () =
    use dir = new TempDir()
    let collectionId = CollectionId "testcode1"
    let feeds = [ "example.com/feed1"; "other.com/feed2" ]

    save dir.Path collectionId feeds

    Assert.Equal(Some feeds, tryLoad dir.Path collectionId)

[<Fact>]
let ``tryLoad returns None for missing file`` () =
    use dir = new TempDir()
    Assert.Equal(None, tryLoad dir.Path (CollectionId "nonexistent"))

[<Fact>]
let ``touch updates last-write-time`` () =
    use dir = new TempDir()
    let collectionId = CollectionId "testcode2"
    save dir.Path collectionId [ "example.com/feed" ]

    let path = OsPath.join dir.Path "testcode2.txt"
    OsFile.setLastWriteTime path (DateTime.Now.AddHours -1.0)
    let before = OsFile.getLastWriteTime path

    touch dir.Path collectionId

    Assert.True(OsFile.getLastWriteTime path > before)

[<Fact>]
let ``delete removes the file`` () =
    use dir = new TempDir()
    let collectionId = CollectionId "testcode3"
    save dir.Path collectionId [ "example.com/feed" ]

    delete dir.Path collectionId

    Assert.False(OsFile.exists (OsPath.join dir.Path "testcode3.txt"))

[<Fact>]
let ``delete is no-op for missing file`` () =
    use dir = new TempDir()
    delete dir.Path (CollectionId "nonexistent")

[<Fact>]
let ``deleteInactive removes files past retention`` () =
    use dir = new TempDir()
    let collectionId = CollectionId "oldfile"
    save dir.Path collectionId [ "example.com/feed" ]
    let path = OsPath.join dir.Path "oldfile.txt"
    OsFile.setLastWriteTime path (DateTime.Now.AddDays -91.0)

    deleteInactive dir.Path (TimeSpan.FromDays 90.0)

    Assert.False(OsFile.exists path)

[<Fact>]
let ``deleteInactive keeps recently accessed files`` () =
    use dir = new TempDir()
    let collectionId = CollectionId "recentfile"
    save dir.Path collectionId [ "example.com/feed" ]

    deleteInactive dir.Path (TimeSpan.FromDays 90.0)

    Assert.True(OsFile.exists (OsPath.join dir.Path "recentfile.txt"))
