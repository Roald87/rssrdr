module SimpleRssServer.Tests.CacheTests

open System
open System.IO
open Xunit

open SimpleRssServer.Cache
open TestHelpers

[<Fact>]
let ``Test isCacheOld returns true for old cache`` () =
    let filePath = "test_cache_file.txt"
    File.WriteAllText(filePath, "Test content")
    File.SetLastWriteTime(filePath, DateTime.Now.AddHours -2.0)

    let result = isCacheOld filePath 1.0

    Assert.True(result, "Expected cache to be old")

    deleteFile filePath

[<Fact>]
let ``Test isCacheOld returns false for recent cache`` () =
    let filePath = "test_cache_file.txt"
    File.WriteAllText(filePath, "Test content")
    File.SetLastWriteTime(filePath, DateTime.Now.AddMinutes -30.0)

    let result = isCacheOld filePath 1.0

    Assert.False(result, "Expected cache to be recent")

    deleteFile filePath
