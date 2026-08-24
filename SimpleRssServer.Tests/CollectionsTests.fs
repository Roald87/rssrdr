module SimpleRssServer.Tests.CollectionsTests

open System
open Xunit

open SimpleRssServer.Collections
open SimpleRssServer.DomainPrimitiveTypes
open TestHelpers

[<Fact>]
let ``generateShortCode produces 11-character base64url string`` () =
    let code = generateShortCode ()
    Assert.Equal(11, code.Length)
    Assert.True(code |> Seq.forall (fun c -> Char.IsLetterOrDigit c || c = '-' || c = '_'))

[<Fact>]
let ``isValidShortCode accepts valid codes`` () =
    Assert.True(isValidShortCode "aBcDeFgHiJk")
    Assert.True(isValidShortCode "abc123")
    Assert.True(isValidShortCode "abc-def_123")

[<Fact>]
let ``isValidShortCode rejects empty string`` () = Assert.False(isValidShortCode "")

[<Fact>]
let ``isValidShortCode rejects path traversal and special characters`` () =
    Assert.False(isValidShortCode "../etc/passwd")
    Assert.False(isValidShortCode "abc/def")
    Assert.False(isValidShortCode "abc?edit")

[<Fact>]
let ``save and tryLoad round-trip a feed list`` () =
    use dir = new TempDir()
    let code = "testcode1"
    let feeds = [ "example.com/feed1"; "other.com/feed2" ]

    save dir.Path code feeds

    Assert.Equal(Some feeds, tryLoad dir.Path code)

[<Fact>]
let ``tryLoad returns None for missing file`` () =
    use dir = new TempDir()
    Assert.Equal(None, tryLoad dir.Path "nonexistent")

[<Fact>]
let ``touch updates last-write-time`` () =
    use dir = new TempDir()
    let code = "testcode2"
    save dir.Path code [ "example.com/feed" ]

    let path = OsPath.join dir.Path (code + ".txt")
    OsFile.setLastWriteTime path (DateTime.Now.AddHours -1.0)
    let before = OsFile.getLastWriteTime path

    touch dir.Path code

    Assert.True(OsFile.getLastWriteTime path > before)

[<Fact>]
let ``delete removes the file`` () =
    use dir = new TempDir()
    let code = "testcode3"
    save dir.Path code [ "example.com/feed" ]

    delete dir.Path code

    Assert.False(OsFile.exists (OsPath.join dir.Path (code + ".txt")))

[<Fact>]
let ``delete is no-op for missing file`` () =
    use dir = new TempDir()
    delete dir.Path "nonexistent"

[<Fact>]
let ``deleteInactive removes files past retention`` () =
    use dir = new TempDir()
    let code = "oldfile"
    save dir.Path code [ "example.com/feed" ]
    let path = OsPath.join dir.Path (code + ".txt")
    OsFile.setLastWriteTime path (DateTime.Now.AddDays -91.0)

    deleteInactive dir.Path (TimeSpan.FromDays 90.0)

    Assert.False(OsFile.exists path)

[<Fact>]
let ``deleteInactive keeps recently accessed files`` () =
    use dir = new TempDir()
    let code = "recentfile"
    save dir.Path code [ "example.com/feed" ]

    deleteInactive dir.Path (TimeSpan.FromDays 90.0)

    Assert.True(OsFile.exists (OsPath.join dir.Path (code + ".txt")))
