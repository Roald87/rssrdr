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
    let filePath = Path.GetRandomFileName()
    let failurePath = failureFilePath filePath

    // Create a failure record 30 minutes ago with 1 failure (should not retry yet)
    let failure1 =
        { LastFailure = DateTimeOffset.Now.AddMinutes(-30.0)
          ConsecutiveFailures = 1 }

    let json1 = JsonSerializer.Serialize(failure1)
    File.WriteAllText(failurePath, json1)

    let result = nextRetry filePath

    match result with
    | Some d -> Assert.True(d > DateTimeOffset.Now, "Can not retry before backoff period")
    | None -> Assert.False(true, "No .faillure file found")

    // Create a failure record 2 hours ago with 1 failure (should retry)
    let failure2 =
        { LastFailure = DateTimeOffset.Now.AddHours(-2.0)
          ConsecutiveFailures = 1 }

    let json2 = JsonSerializer.Serialize(failure2)
    File.WriteAllText(failurePath, json2)

    let result = nextRetry filePath

    match result with
    | Some d -> Assert.True(d < DateTimeOffset.Now, "Can retry after backoff period")
    | None -> Assert.False(true, "No .faillure file found")

    // Cleanup
    deleteFile failurePath

[<Fact>]
let ``Test shouldRetry with corrupted failure file`` () =
    let filePath = Path.GetRandomFileName()
    let failurePath = failureFilePath filePath

    // Create corrupted failure record
    File.WriteAllText(failurePath, "not valid json")

    let result = nextRetry filePath
    Assert.True(result.IsNone, "Expected None for corrupted failure file")

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
let ``Test cacheAge returns age for existing file`` () =
    let filePath = Path.GetRandomFileName()
    File.WriteAllText(filePath, "Test content")
    let age = DateTime.Now.AddHours -2
    File.SetLastWriteTime(filePath, age)

    let result = fileLastModifued filePath

    Assert.Equal(age |> DateTimeOffset, result.Value)

    deleteFile filePath

[<Fact>]
let ``Test cacheAge returns None for non existing cache`` () =
    let filePath = Path.GetRandomFileName()

    let result = fileLastModifued filePath

    Assert.True(result.IsNone, "Expected cache Age to be none")

    deleteFile filePath
