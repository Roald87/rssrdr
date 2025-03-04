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

[<Theory>]
[<InlineData("info", LogLevel.Information)>]
[<InlineData("warning", LogLevel.Warning)>]
[<InlineData("error", LogLevel.Error)>]
[<InlineData("debug", LogLevel.Debug)>]
let ``parse should correctly parse --loglevel arguments`` (loglevel: string, expectedLogLevel: LogLevel) =
    let input = $"--loglevel {loglevel}"
    let expected = { Hostname = None; Loglevel = Some expectedLogLevel }
    let result = parse input
    Assert.Equal(expected, result)

[<Fact>]
let ``parse should correctly parse --hostname argument`` () =
    let input = "--hostname http://+:1234"
    let expected = { Hostname = Some "http://+:1234"; Loglevel = None }
    let result = parse input
    Assert.Equal(expected, result)
