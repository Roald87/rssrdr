module ArgParserTests

open Xunit
open ArgParser
open Microsoft.Extensions.Logging

[<Fact>]
let ``parse should return Help for --help argument`` () =
    let input = "--help"
    let expected = Help
    let result = parse input
    Assert.Equal(expected, result)

[<Fact>]
let ``parse should correctly parse --hostname and --loglevel arguments`` () =
    let input = "--hostname http://0.0.0.0:12345 --loglevel debug"

    let expected =
        Args
            { Hostname = Some "http://0.0.0.0:12345"
              LogLevel = Some LogLevel.Debug }

    let result = parse input
    Assert.Equal(expected, result)

[<Fact>]
let ``parse should fail with error message for invalid --loglevel`` () =
    let input = "--loglevel doesnt_exist"
    let ex = Assert.Throws<System.Exception>(fun () -> parse input |> ignore)
    Assert.Equal("Log level doesnt_exist does not exist", ex.Message)

[<Theory>]
[<InlineData("info", LogLevel.Information)>]
[<InlineData("warning", LogLevel.Warning)>]
[<InlineData("error", LogLevel.Error)>]
[<InlineData("debug", LogLevel.Debug)>]
let ``parse should correctly parse --loglevel arguments`` (loglevel: string, expectedLogLevel: LogLevel) =
    let input = $"--loglevel {loglevel}"

    let expected =
        Args
            { Hostname = None
              LogLevel = Some expectedLogLevel }

    let result = parse input
    Assert.Equal(expected, result)

[<Fact>]
let ``parse should correctly parse --hostname argument`` () =
    let input = "--hostname http://+:1234"

    let expected =
        Args
            { Hostname = Some "http://+:1234"
              LogLevel = None }

    let result = parse input
    Assert.Equal(expected, result)
