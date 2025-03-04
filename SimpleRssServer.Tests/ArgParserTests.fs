module ArgParserTests

open Xunit
open ArgParser

[<Fact>]
let ``parse should return ParsedArgs with the given string`` () =
    let input = "test"
    let expected = { Argument = input }
    let result = parse input
    Assert.Equal(expected, result)

[<Fact>]
let ``parse should correctly parse --hostname argument`` () =
    let input = "--hostname http://+:1234"
    let expected = { Hostname = "http://+:1234" }
    let result = parse input
    Assert.Equal(expected, result)
