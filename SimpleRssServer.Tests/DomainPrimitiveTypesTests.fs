module DomainPrimitiveTypesTests

open Xunit
open System
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.Tests.TestHelpers

[<Fact>]
let ``Uri.create should return Ok for valid URI with dot in host`` () =
    let input = "https://example.com"
    let result = FeedUri.create input

    match result with
    | Ok uri -> Assert.Equal("https://example.com/", uri.ToString())
    | Error error -> failwithf $"Expected Ok but got Error: {error}"

[<Fact>]
let ``Uri.create should return Error for valid URI without dot in host`` () =
    let input = "https://localhost"
    let result = FeedUri.create input

    match result with
    | Error(HostNameMustContainDot invalid) -> Assert.Equal("https://localhost", invalid.Value)
    | Ok _ -> failwith "Expected Error but got Ok"
    | Error error -> failwithf $"Expected HostNameMustContainDot error but got {error}"

[<Fact>]
let ``Uri.create should return Error for invalid URI format`` () =
    let input = "not a uri"
    let result = FeedUri.create input

    match result with
    | Error(UriFormatException(invalid, _)) -> Assert.Equal("not a uri", invalid.Value)
    | Ok x -> failwithf $"Expected Error but got Ok {x}"
    | Error error -> failwithf $"Expected UriFormatException error but got {error}"

[<Fact>]
let ``Uri.createWithHttps should add https to URL without scheme`` () =
    let input = "example.com"
    let result = FeedUri.createWithHttps input

    match result with
    | Ok uri -> Assert.Equal("https://example.com/", uri.ToString())
    | Error error -> failwithf $"Expected Ok but got Error: {error}"

[<Fact>]
let ``Uri.createWithHttps should keep http scheme`` () =
    let input = "http://example.com"
    let result = FeedUri.createWithHttps input

    match result with
    | Ok uri -> Assert.Equal("http://example.com/", uri.ToString())
    | Error error -> failwithf $"Expected Ok but got Error: {error}"

[<Fact>]
let ``Uri.createWithHttps should keep https scheme`` () =
    let input = "https://example.com"
    let result = FeedUri.createWithHttps input

    match result with
    | Ok uri -> Assert.Equal("https://example.com/", uri.ToString())
    | Error error -> failwithf $"Expected Ok but got Error: {error}"

[<Fact>]
let ``Uri.createWithHttps should return Error for invalid host`` () =
    let input = "localhost"
    let result = FeedUri.createWithHttps input

    match result with
    | Error(HostNameMustContainDot invalid) -> Assert.Equal("https://localhost", invalid.Value)
    | Ok x -> failwith $"Expected Error but got Ok {x}"
    | Error error -> failwithf $"Expected HostNameMustContainDot error but got {error}"

[<Fact>]
let ``Uri.StripScheme removes https scheme`` () =
    Assert.Equal("example.com/feed", FeedUri.removeScheme "https://example.com/feed")

[<Fact>]
let ``Uri.StripScheme removes http scheme`` () =
    Assert.Equal("example.com/feed", FeedUri.removeScheme "http://example.com/feed")

[<Fact>]
let ``Uri.StripScheme leaves url without scheme unchanged`` () =
    Assert.Equal("example.com/feed", FeedUri.removeScheme "example.com/feed")

[<Fact>]
let ``Query.Create empty string gives empty ToString`` () =
    let q = Query.Create ""
    Assert.Empty(q.GetValues "rss")
    Assert.Equal("", q |> string)

[<Fact>]
let ``Query.Create single rss param round-trips`` () =
    let q = Query.Create "?rss=example.com/feed"
    let values = q.GetValues "rss"
    Assert.Equal(1, values.Length)
    Assert.Equal("example.com/feed", values[0])
    Assert.Equal("?rss=example.com/feed", q |> string)

[<Fact>]
let ``Query.Create two rss params round-trip`` () =
    let q = Query.Create "?rss=example.com/feed&rss=other.com/feed"
    let values = q.GetValues "rss"
    Assert.Equal(2, values.Length)
    Assert.Contains("example.com/feed", values)
    Assert.Contains("other.com/feed", values)
    let s = q |> string
    Assert.Contains("rss=example.com/feed", s)
    Assert.Contains("rss=other.com/feed", s)

[<Fact>]
let ``Query.Create preserves non-rss params`` () =
    let q = Query.Create "?rss=example.com/feed&foo=bar"
    let rssValues = q.GetValues "rss"
    Assert.Equal(1, rssValues.Length)
    Assert.Equal("example.com/feed", rssValues[0])
    let fooValues = q.GetValues "foo"
    Assert.Equal(1, fooValues.Length)
    Assert.Equal("bar", fooValues[0])
    let s = q |> string
    Assert.Contains("rss=example.com/feed", s)
    Assert.Contains("foo=bar", s)

[<Fact>]
let ``Query.Create returns empty list for missing key`` () =
    let q = Query.Create "?rss=example.com/feed"
    Assert.Empty(q.GetValues "missing")

[<Fact>]
let ``Query.CreateWithKey single value`` () =
    let q = Query.CreateWithKey("rss", [ "example.com/feed" ])
    let values = q.GetValues "rss"
    Assert.Equal(1, values.Length)
    Assert.Equal("example.com/feed", values[0])
    Assert.Equal("?rss=example.com/feed", q |> string)

[<Fact>]
let ``Query.CreateWithKey multiple values`` () =
    let q = Query.CreateWithKey("rss", [ "example.com/feed"; "other.com/feed" ])
    let values = q.GetValues "rss"
    Assert.Equal(2, values.Length)
    Assert.Contains("example.com/feed", values)
    Assert.Contains("other.com/feed", values)
    let s = q |> string
    Assert.Contains("rss=example.com/feed", s)
    Assert.Contains("rss=other.com/feed", s)

[<Fact>]
let ``Query.CreateWithKey empty list gives empty ToString`` () =
    let q = Query.CreateWithKey("rss", [])
    Assert.Empty(q.GetValues "rss")
    Assert.Equal("", q |> string)

[<Fact>]
let ``OsDirectory.getFiles returns OsPath array`` () =
    use tmp = new TempDir()
    let file1 = OsPath.join tmp.Path "file1.txt"
    let file2 = OsPath.join tmp.Path "file2.txt"
    OsFile.writeAllLines file1 [ "a" ]
    OsFile.writeAllLines file2 [ "b" ]

    let files = OsDirectory.getFiles tmp.Path

    Assert.Equal(2, files.Length)
    Assert.True(Array.contains file1 files)
    Assert.True(Array.contains file2 files)
