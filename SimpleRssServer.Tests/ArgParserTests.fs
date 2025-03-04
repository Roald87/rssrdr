module ArgParserTests

open Xunit
open ArgParser
open Microsoft.Extensions.Logging

[<Fact>]
let ``parse should return ParsedArgs with the given string`` () =
    let input = "test"
    let expected = { Hostname = None; Loglevel = None }
    let result = parse input
    Assert.Equal(expected, result)

[<Fact>]
let ``parse should correctly parse --loglevel info argument`` () =
    let input = "--loglevel info"
    let expected = { Hostname = None; Loglevel = Some LogLevel.Information }
    let result = parse input
    Assert.Equal(expected, result)

[<Fact>]
let ``parse should correctly parse --loglevel warning argument`` () =
    let input = "--loglevel warning"
    let expected = { Hostname = None; Loglevel = Some LogLevel.Warning }
    let result = parse input
    Assert.Equal(expected, result)

[<Fact>]
let ``parse should correctly parse --loglevel error argument`` () =
    let input = "--loglevel error"
    let expected = { Hostname = None; Loglevel = Some LogLevel.Error }
    let result = parse input
    Assert.Equal(expected, result)

[<Fact>]
let ``parse should correctly parse --loglevel argument`` () =
    let input = "--loglevel debug"
    let expected = { Hostname = None; Loglevel = Some LogLevel.Debug }
    let result = parse input
    Assert.Equal(expected, result)

[<Fact>]
let ``parse should correctly parse --hostname argument`` () =
    let input = "--hostname http://+:1234"
    let expected = { Hostname = Some "http://+:1234"; Loglevel = None }
    let result = parse input
    Assert.Equal(expected, result)
