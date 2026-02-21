module DomainPrimitiveTypesTests

open Xunit
open System
open SimpleRssServer.DomainPrimitiveTypes

[<Fact>]
let ``Uri.create should return Ok for valid URI with dot in host`` () =
    let input = "https://example.com"
    let result = Uri.create input

    match result with
    | Ok uri -> Assert.Equal("https://example.com/", uri.ToString())
    | Error _ -> Assert.True(false, "Expected Ok")

[<Fact>]
let ``Uri.create should return Error for valid URI without dot in host`` () =
    let input = "https://localhost"
    let result = Uri.create input

    match result with
    | Ok _ -> Assert.True(false, "Expected Error")
    | Error(HostNameMustContainDot invalid) -> Assert.Equal("https://localhost", InvalidUri.value invalid)
    | Error _ -> Assert.True(false, "Expected HostNameMustContainDot")

[<Fact>]
let ``Uri.create should return Error for invalid URI format`` () =
    let input = "not a uri"
    let result = Uri.create input

    match result with
    | Ok _ -> Assert.True(false, "Expected Error")
    | Error(UriFormatException(invalid, _)) -> Assert.Equal("not a uri", InvalidUri.value invalid)
    | Error _ -> Assert.True(false, "Expected UriFormatException")

[<Fact>]
let ``Uri.createWithHttps should add https to URL without scheme`` () =
    let input = "example.com"
    let result = Uri.createWithHttps input

    match result with
    | Ok uri -> Assert.Equal("https://example.com/", uri.ToString())
    | Error _ -> Assert.True(false, "Expected Ok")

[<Fact>]
let ``Uri.createWithHttps should keep http scheme`` () =
    let input = "http://example.com"
    let result = Uri.createWithHttps input

    match result with
    | Ok uri -> Assert.Equal("http://example.com/", uri.ToString())
    | Error _ -> Assert.True(false, "Expected Ok")

[<Fact>]
let ``Uri.createWithHttps should keep https scheme`` () =
    let input = "https://example.com"
    let result = Uri.createWithHttps input

    match result with
    | Ok uri -> Assert.Equal("https://example.com/", uri.ToString())
    | Error _ -> Assert.True(false, "Expected Ok")

[<Fact>]
let ``Uri.createWithHttps should return Error for invalid host`` () =
    let input = "localhost"
    let result = Uri.createWithHttps input

    match result with
    | Ok _ -> Assert.True(false, "Expected Error")
    | Error(HostNameMustContainDot invalid) -> Assert.Equal("https://localhost", InvalidUri.value invalid)
    | Error _ -> Assert.True(false, "Expected HostNameMustContainDot")
