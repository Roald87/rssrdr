module DomainPrimitiveTypesTests

open Xunit
open System
open SimpleRssServer.DomainPrimitiveTypes

[<Fact>]
let ``Uri.create should return Ok for valid URI with dot in host`` () =
    let input = "https://example.com"
    let result = Uri.Create input

    match result with
    | Ok uri -> Assert.Equal("https://example.com/", uri.ToString())
    | Error error -> failwithf $"Expected Ok but got Error: {error}"

[<Fact>]
let ``Uri.create should return Error for valid URI without dot in host`` () =
    let input = "https://localhost"
    let result = Uri.Create input

    match result with
    | Error(HostNameMustContainDot invalid) -> Assert.Equal("https://localhost", invalid.Value)
    | Ok _ -> failwith "Expected Error but got Ok"
    | Error error -> failwithf $"Expected HostNameMustContainDot error but got {error}"

[<Fact>]
let ``Uri.create should return Error for invalid URI format`` () =
    let input = "not a uri"
    let result = Uri.Create input

    match result with
    | Error(UriFormatException(invalid, _)) -> Assert.Equal("not a uri", invalid.Value)
    | Ok x -> failwithf $"Expected Error but got Ok {x}"
    | Error error -> failwithf $"Expected UriFormatException error but got {error}"

[<Fact>]
let ``Uri.createWithHttps should add https to URL without scheme`` () =
    let input = "example.com"
    let result = Uri.CreateWithHttps input

    match result with
    | Ok uri -> Assert.Equal("https://example.com/", uri.ToString())
    | Error error -> failwithf $"Expected Ok but got Error: {error}"

[<Fact>]
let ``Uri.createWithHttps should keep http scheme`` () =
    let input = "http://example.com"
    let result = Uri.CreateWithHttps input

    match result with
    | Ok uri -> Assert.Equal("http://example.com/", uri.ToString())
    | Error error -> failwithf $"Expected Ok but got Error: {error}"

[<Fact>]
let ``Uri.createWithHttps should keep https scheme`` () =
    let input = "https://example.com"
    let result = Uri.CreateWithHttps input

    match result with
    | Ok uri -> Assert.Equal("https://example.com/", uri.ToString())
    | Error error -> failwithf $"Expected Ok but got Error: {error}"

[<Fact>]
let ``Uri.createWithHttps should return Error for invalid host`` () =
    let input = "localhost"
    let result = Uri.CreateWithHttps input

    match result with
    | Error(HostNameMustContainDot invalid) -> Assert.Equal("https://localhost", invalid.Value)
    | Ok x -> failwith $"Expected Error but got Ok {x}"
    | Error error -> failwithf $"Expected HostNameMustContainDot error but got {error}"

[<Fact>]
let ``Uri.StripScheme removes https scheme`` () =
    Assert.Equal("example.com/feed", Uri.StripScheme "https://example.com/feed")

[<Fact>]
let ``Uri.StripScheme removes http scheme`` () =
    Assert.Equal("example.com/feed", Uri.StripScheme "http://example.com/feed")

[<Fact>]
let ``Uri.StripScheme leaves url without scheme unchanged`` () =
    Assert.Equal("example.com/feed", Uri.StripScheme "example.com/feed")

[<Fact>]
let ``Query.Create empty string gives empty ToString`` () =
    Assert.Equal("", Query.Create "" |> string)

[<Fact>]
let ``Query.Create single rss param round-trips`` () =
    let q = Query.Create "?rss=example.com/feed" |> string
    Assert.Equal("?rss=example.com/feed", q)

[<Fact>]
let ``Query.Create two rss params round-trip`` () =
    let q = Query.Create "?rss=example.com/feed&rss=other.com/feed" |> string
    Assert.Contains("rss=example.com/feed", q)
    Assert.Contains("rss=other.com/feed", q)

[<Fact>]
let ``Query.Create preserves non-rss params`` () =
    let q = Query.Create "?rss=example.com/feed&foo=bar" |> string
    Assert.Contains("rss=", q)
    Assert.Contains("foo=bar", q)
