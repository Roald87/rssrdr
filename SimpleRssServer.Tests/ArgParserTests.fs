module ArgParserTests

open Xunit
open ArgParser

[<Fact>]
let ``parse should return ParsedArgs with the given string`` () =
    let input = "test"
    let expected = { Argument = input }
    let result = parse input
    Assert.Equal(expected, result)
