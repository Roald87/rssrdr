module SimpleRssServer.Tests.RequestTests

open System
open Xunit

open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.Request
open TestHelpers

[<Fact>]
let ``Test getRssUrls`` () =
    let result = getRssUrls "?rss=https://abs.com/test"

    Assert.Equal<Result<Uri, UriError> list>([ Ok(Uri "https://abs.com/test") ], result)

[<Fact>]
let ``Test getRssUrls with other key should return empty list`` () =
    let result = getRssUrls "?foo=example.com"

    Assert.Equal<Result<Uri, UriError> list>([], result)

[<Fact>]
let ``Test getRssUrls with two URLs`` () =
    let result = getRssUrls "?rss=https://abs.com/test1&rss=https://abs.com/test2"

    let expected = [ Ok(Uri "https://abs.com/test1"); Ok(Uri "https://abs.com/test2") ]

    Assert.Equal<Result<Uri, UriError> list>(expected, result)

[<Fact>]
let ``Test getRssUrls with empty string`` () =
    let result = getRssUrls ""

    Assert.Equal<Result<Uri, UriError> list>([], result)

[<Fact>]
let ``Test getRssUrls with invalid URL`` () =
    let result = getRssUrls "?rss=invalid-url"
    Assert.Equal(1, result.Length)

    match result.[0] with
    | Error(HostNameMustContainDot url) -> Assert.Contains("invalid-url", url.Value)
    | x -> failwithf $"Expected Error HostNameMustContainDot, but got {x}"

[<Fact>]
let ``Test getRssUrls with valid and invalid URLs`` () =
    let result = getRssUrls "?rss=invalid-url&rss=https://valid-url.com"
    Assert.Equal(2, result.Length)

    match result.[0] with
    | Error(HostNameMustContainDot url) -> Assert.Contains("invalid-url", url.Value)
    | x -> failwithf $"Expected Error HostNameMustContainDot, but got {x}"

    Assert.Equal(Uri "https://valid-url.com", getOk result.[1])

[<Fact>]
let ``Test getRssUrls adds https if missing`` () =
    let result = getRssUrls "?rss=example.com/feed&rss=http://example.com/feed2"

    let expected =
        [ Ok(Uri "https://example.com/feed"); Ok(Uri "http://example.com/feed2") ]

    Assert.Equal<Result<Uri, UriError> list>(expected, result)
