module SimpleRssServer.Tests.CacheTests

open System
open System.IO
open Xunit

open SimpleRssServer.Cache
open TestHelpers
open System.Text.Json

[<Fact>]
let ``Test writeCache clears failure record`` () =
    let filePath = "test_cache_file.txt"
    let failurePath = failureFilePath filePath

    // Create a failure record
    let failure =
        { LastFailure = DateTimeOffset.Now
          ConsecutiveFailures = 2 }

    let json = JsonSerializer.Serialize(failure)
    File.WriteAllText(failurePath, json)

    // Write to cache
    writeCache filePath "test content" |> Async.RunSynchronously

    // Verify failure record is cleared
    Assert.False(File.Exists failurePath, "Expected failure record to be deleted")

    // Cleanup
    deleteFile filePath
    deleteFile failurePath

[<Fact>]
let ``Test recordFailure tracks consecutive failures`` () =
    let cachePath = "test_cache"
    Directory.CreateDirectory(cachePath) |> ignore
    let filePath = Path.Combine(cachePath, "test_cache_file.txt")
    let failurePath = failureFilePath filePath

    // Record first failure
    recordFailure filePath |> Async.RunSynchronously

    let failure1 =
        JsonSerializer.Deserialize<FetchFailure>(File.ReadAllText failurePath)

    Assert.Equal(1, failure1.ConsecutiveFailures)

    // Record second failure
    recordFailure filePath |> Async.RunSynchronously

    let failure2 =
        JsonSerializer.Deserialize<FetchFailure>(File.ReadAllText failurePath)

    Assert.Equal(2, failure2.ConsecutiveFailures)

    // Cleanup
    deleteFile failurePath
    Directory.Delete(cachePath, true)

[<Fact>]
let ``Test shouldRetry respects backoff periods`` () =
    let filePath = "test_cache_file.txt"
    let failurePath = failureFilePath filePath

    // Create a failure record 30 minutes ago with 1 failure (should not retry yet)
    let failure1 =
        { LastFailure = DateTimeOffset.Now.AddMinutes(-30.0)
          ConsecutiveFailures = 1 }

    let json1 = JsonSerializer.Serialize(failure1)
    File.WriteAllText(failurePath, json1)

    Assert.False(shouldRetry filePath, "Should not retry before backoff period")

    // Create a failure record 2 hours ago with 1 failure (should retry)
    let failure2 =
        { LastFailure = DateTimeOffset.Now.AddHours(-2.0)
          ConsecutiveFailures = 1 }

    let json2 = JsonSerializer.Serialize(failure2)
    File.WriteAllText(failurePath, json2)

    Assert.True(shouldRetry filePath, "Should retry after backoff period")

    // Cleanup
    deleteFile failurePath

[<Fact>]
let ``Test shouldRetry with corrupted failure file`` () =
    let filePath = "test_cache_file.txt"
    let failurePath = failureFilePath filePath

    // Create corrupted failure record
    File.WriteAllText(failurePath, "not valid json")

    Assert.True(shouldRetry filePath, "Should allow retry with corrupted failure file")

    // Cleanup
    deleteFile failurePath

[<Fact>]
let ``Test getBackoffHours follows exponential pattern`` () =
    Assert.Equal(1.0, getBackoffHours 1)
    Assert.Equal(2.0, getBackoffHours 2)
    Assert.Equal(4.0, getBackoffHours 3)
    Assert.Equal(8.0, getBackoffHours 4)
    Assert.Equal(16.0, getBackoffHours 5)
    Assert.Equal(24.0, getBackoffHours 6) // Should cap at 24 hours
    Assert.Equal(24.0, getBackoffHours 7) // Should stay capped

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

[<Fact>]
let ``Test isCacheOld returns true for non existing cache`` () =
    let filePath = "doesnt_exist.txt"

    let result = isCacheOld filePath 1.0

    Assert.True(result, "Expected cache to be old")

    deleteFile filePath
