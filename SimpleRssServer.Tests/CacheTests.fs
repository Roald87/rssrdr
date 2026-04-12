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
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path

    // Create a failure record
    let failure =
        { LastFailure = DateTimeOffset.Now
          ConsecutiveFailures = 2 }

    let json = JsonSerializer.Serialize failure
    OsFile.writeAllText failurePath json

    // Clear the failure record explicitly
    clearFailure tmp.Path

    // Verify failure record is cleared
    Assert.False(OsFile.exists failurePath, "Expected failure record to be deleted by clearFailure")

[<Fact>]
let ``Test recordFailure tracks consecutive failures`` () =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path

    // Record first failure
    recordFailure NullLogger.Instance tmp.Path

    let failure1 =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(1, failure1.ConsecutiveFailures)

    // Record second failure
    recordFailure NullLogger.Instance tmp.Path

    let failure2 =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(2, failure2.ConsecutiveFailures)

[<Fact>]
let ``Test get retry periods from failure file`` () =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path

    let failure1 =
        { LastFailure = DateTimeOffset.Now.AddMinutes -30.0
          ConsecutiveFailures = 1 }

    let json1 = JsonSerializer.Serialize failure1
    OsFile.writeAllText failurePath json1

    let result = nextRetry NullLogger.Instance tmp.Path

    match result with
    | Some d -> Assert.True(d > DateTimeOffset.Now, "Backoff period should not have passed yet")
    | None -> failwithf $"No .faillure file found at {failurePath}"

    let failure2 =
        { LastFailure = DateTimeOffset.Now.AddHours -2.0
          ConsecutiveFailures = 1 }

    let json2 = JsonSerializer.Serialize failure2
    OsFile.writeAllText failurePath json2

    let result = nextRetry NullLogger.Instance tmp.Path

    match result with
    | Some d -> Assert.True(d < DateTimeOffset.Now, "Backoff period should have passed")
    | None -> failwithf $"No .faillure file found at {failurePath}"

[<Fact>]
let ``Test shouldRetry with corrupted failure file`` () =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path

    // Create corrupted failure record
    OsFile.writeAllText failurePath "not valid json"

    let result = nextRetry NullLogger.Instance tmp.Path
    Assert.True(result.IsNone, "Expected None for corrupted failure file")

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
    use tmp = new TempPath()
    OsFile.writeAllText tmp.Path "Test content"
    let age = DateTime.Now.AddHours -2
    OsFile.setLastWriteTime tmp.Path age

    let result = fileLastModified tmp.Path

    Assert.Equal(age |> DateTimeOffset, result.Value)

[<Fact>]
let ``Test cacheAge returns None for non existing cache`` () =
    use tmp = new TempPath()

    let result = fileLastModified tmp.Path

    Assert.True(result.IsNone, "Expected cache Age to be none")

[<Fact>]
let ``Test clearExpiredCache removes files older than retention`` () =
    use tmp = new TempDir()

    let oldFile = OsPath.join tmp.Path "old_cache"
    let recentFile = OsPath.join tmp.Path "recent_cache"

    // Create old file (10 days old)
    OsFile.writeAllText oldFile "old content"
    OsFile.setLastWriteTime oldFile (DateTime.Now.AddDays -10.0)

    // Create recent file (3 days old)
    OsFile.writeAllText recentFile "recent content"
    OsFile.setLastWriteTime recentFile (DateTime.Now.AddDays -3.0)

    let retention = TimeSpan.FromDays 7.0

    // Act
    clearExpiredCache NullLogger.Instance tmp.Path retention

    // Assert
    Assert.False(OsFile.exists oldFile, "Expected old cache file to be deleted")
    Assert.True(OsFile.exists recentFile, "Expected recent cache file to be kept")

[<Fact>]
let ``Test clearExpiredCache also removes failure files`` () =
    use tmp = new TempDir()

    let oldFile = OsPath.join tmp.Path "old_cache"
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
    clearExpiredCache NullLogger.Instance tmp.Path retention

    // Assert
    Assert.False(OsFile.exists oldFile, "Expected old cache file to be deleted")
    Assert.False(OsFile.exists failureFile, "Expected failure file to be deleted")

[<Fact>]
let ``Test clearExpiredCache skips non-existent directory`` () =
    let cacheDir = OsPath "non_existent_cache_dir"
    let retention = TimeSpan.FromDays 7.0

    // This should not throw an exception
    clearExpiredCache NullLogger.Instance cacheDir retention
    Assert.True(true, "Expected clearExpiredCache to handle non-existent directory gracefully")

[<Fact>]
let ``Test clearExpiredCache keeps empty directory`` () =
    use tmp = new TempDir()

    let retention = TimeSpan.FromDays 7.0

    // Act
    clearExpiredCache NullLogger.Instance tmp.Path retention

    // Assert
    Assert.True(OsDirectory.exists tmp.Path, "Expected empty cache directory to still exist")

[<Fact>]
let ``Test convertUrlToFilename`` () =
    Assert.Equal(Filename "https_abc_com_test", convertUrlToValidFilename (Uri "https://abc.com/test"))

    Assert.Equal(
        Filename "https_abc_com_test_rss_blabla",
        convertUrlToValidFilename (Uri "https://abc.com/test?rss=blabla")
    )
