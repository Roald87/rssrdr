module SimpleRssServer.Tests.CacheTests

open System
open System.IO
open Xunit
open SimpleRssServer.Cache

[<Fact>]
let ``Test isCacheOld returns true for old cache`` () =
    let filePath = "test_cache_file.txt"
    File.WriteAllText(filePath, "Test content")
    File.SetLastWriteTime(filePath, DateTime.Now.AddHours -2.0)

    let result = isCacheOld filePath 1.0

    Assert.True(result, "Expected cache to be old")

    if File.Exists filePath then
        File.Delete filePath

[<Fact>]
let ``Test isCacheOld returns false for recent cache`` () =
    let filePath = "test_cache_file.txt"
    File.WriteAllText(filePath, "Test content")
    File.SetLastWriteTime(filePath, DateTime.Now.AddMinutes -30.0)

    let result = isCacheOld filePath 1.0

    Assert.False(result, "Expected cache to be recent")

    if File.Exists filePath then
        File.Delete filePath
