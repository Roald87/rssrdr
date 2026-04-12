module SimpleRssServer.Tests.CacheTests

open Microsoft.Extensions.Logging.Abstractions
open System
open System.IO
open System.Text.Json
open Xunit

open SimpleRssServer.Cache
open SimpleRssServer.DomainPrimitiveTypes
open TestHelpers

[<Fact>]
let ``Test clearFailure deletes failure record`` () =
    let filePath = OsPath "test_cache_file.txt"
    let failurePath = failureFilePath filePath

    // Create a failure record
    let failure =
        { LastFailure = DateTimeOffset.Now
          ConsecutiveFailures = 2 }

    let json = JsonSerializer.Serialize failure
    OsFile.writeAllText failurePath json

    // Clear the failure record explicitly
    clearFailure filePath

    // Verify failure record is cleared
    Assert.False(OsFile.exists failurePath, "Expected failure record to be deleted by clearFailure")

    // Cleanup
    deleteFile filePath
    deleteFile failurePath

[<Fact>]
let ``Test recordFailure tracks consecutive failures`` () =
    let filePath = OsPath(Path.GetRandomFileName())
    let failurePath = failureFilePath filePath

    // Record first failure
    recordFailure NullLogger.Instance filePath

    let failure1 =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(1, failure1.ConsecutiveFailures)

    // Record second failure
    recordFailure NullLogger.Instance filePath

    let failure2 =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(2, failure2.ConsecutiveFailures)

    // Cleanup
    deleteFile failurePath

[<Fact>]
let ``Test get retry periods from failure file`` () =
    let filePath = OsPath(Path.GetRandomFileName())
    let failurePath = failureFilePath filePath

    let failure1 =
        { LastFailure = DateTimeOffset.Now.AddMinutes -30.0
          ConsecutiveFailures = 1 }

    let json1 = JsonSerializer.Serialize failure1
    OsFile.writeAllText failurePath json1

    let result = nextRetry NullLogger.Instance filePath

    match result with
    | Some d -> Assert.True(d > DateTimeOffset.Now, "Backoff period should not have passed yet")
    | None -> failwithf $"No .faillure file found at {failurePath}"

    let failure2 =
        { LastFailure = DateTimeOffset.Now.AddHours -2.0
          ConsecutiveFailures = 1 }

    let json2 = JsonSerializer.Serialize failure2
    OsFile.writeAllText failurePath json2

    let result = nextRetry NullLogger.Instance filePath

    match result with
    | Some d -> Assert.True(d < DateTimeOffset.Now, "Backoff period should have passed")
    | None -> failwithf $"No .faillure file found at {failurePath}"

    // Cleanup
    deleteFile failurePath

[<Fact>]
let ``Test shouldRetry with corrupted failure file`` () =
    let filePath = OsPath(Path.GetRandomFileName())
    let failurePath = failureFilePath filePath

    // Create corrupted failure record
    OsFile.writeAllText failurePath "not valid json"

    let result = nextRetry NullLogger.Instance filePath
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
let ``Test fileLastModifued returns age for existing file`` () =
    let filePath = OsPath(Path.GetRandomFileName())
    OsFile.writeAllText filePath "Test content"
    let age = DateTime.Now.AddHours -2
    OsFile.setLastWriteTime filePath age

    let result = fileLastModified filePath

    Assert.Equal(age |> DateTimeOffset, result.Value)

    deleteFile filePath

[<Fact>]
let ``Test cacheAge returns None for non existing cache`` () =
    let filePath = OsPath(Path.GetRandomFileName())

    let result = fileLastModified filePath

    Assert.True(result.IsNone, "Expected cache Age to be none")

    deleteFile filePath

[<Fact>]
let ``Test clearExpiredCache removes files older than retention`` () =
    let cacheDir = OsPath "test_cache_cleanup"
    OsDirectory.create cacheDir

    let oldFile = OsPath.join cacheDir "old_cache"
    let recentFile = OsPath.join cacheDir "recent_cache"

    // Create old file (10 days old)
    OsFile.writeAllText oldFile "old content"
    OsFile.setLastWriteTime oldFile (DateTime.Now.AddDays -10.0)

    // Create recent file (3 days old)
    OsFile.writeAllText recentFile "recent content"
    OsFile.setLastWriteTime recentFile (DateTime.Now.AddDays -3.0)

    let retention = TimeSpan.FromDays 7.0

    // Act
    clearExpiredCache NullLogger.Instance cacheDir retention

    // Assert
    Assert.False(OsFile.exists oldFile, "Expected old cache file to be deleted")
    Assert.True(OsFile.exists recentFile, "Expected recent cache file to be kept")

    // Cleanup
    OsDirectory.deleteRecursive cacheDir

[<Fact>]
let ``Test clearExpiredCache also removes failure files`` () =
    let cacheDir = OsPath "test_cache_cleanup_failures"
    OsDirectory.create cacheDir

    let oldFile = OsPath.join cacheDir "old_cache"
    let failureFile = failureFilePath oldFile

    // Create old cache file and its failure record
    OsFile.writeAllText oldFile "old content"
    OsFile.setLastWriteTime oldFile (DateTime.Now.AddDays -10.0)

    let failure =
        { LastFailure = DateTimeOffset.Now.AddDays -10.0
          ConsecutiveFailures = 3 }

    let json = JsonSerializer.Serialize failure
    OsFile.writeAllText failureFile json
    OsFile.setLastWriteTime failureFile (DateTime.Now.AddDays -10.0)

    let retention = TimeSpan.FromDays 7.0

    // Act
    clearExpiredCache NullLogger.Instance cacheDir retention

    // Assert
    Assert.False(OsFile.exists oldFile, "Expected old cache file to be deleted")
    Assert.False(OsFile.exists failureFile, "Expected failure file to be deleted")

    // Cleanup
    OsDirectory.deleteRecursive cacheDir

[<Fact>]
let ``Test clearExpiredCache skips non-existent directory`` () =
    let cacheDir = OsPath "non_existent_cache_dir"
    let retention = TimeSpan.FromDays 7.0

    // This should not throw an exception
    clearExpiredCache NullLogger.Instance cacheDir retention
    Assert.True(true, "Expected clearExpiredCache to handle non-existent directory gracefully")

[<Fact>]
let ``Test clearExpiredCache keeps empty directory`` () =
    let cacheDir = OsPath "test_empty_cache"
    OsDirectory.create cacheDir

    let retention = TimeSpan.FromDays 7.0

    // Act
    clearExpiredCache NullLogger.Instance cacheDir retention

    // Assert
    Assert.True(OsDirectory.exists cacheDir, "Expected empty cache directory to still exist")

    // Cleanup
    OsDirectory.deleteRecursive cacheDir

[<Fact>]
let ``Test convertUrlToFilename`` () =
    Assert.Equal(Filename "https_abc_com_test", convertUrlToValidFilename (Uri "https://abc.com/test"))

    Assert.Equal(
        Filename "https_abc_com_test_rss_blabla",
        convertUrlToValidFilename (Uri "https://abc.com/test?rss=blabla")
    )
