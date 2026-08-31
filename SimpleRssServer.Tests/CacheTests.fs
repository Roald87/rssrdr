module SimpleRssServer.Tests.CacheTests

open Microsoft.Extensions.Logging.Abstractions
open System
open System.Net
open System.Text.Json
open Xunit

open SimpleRssServer.Cache
open SimpleRssServer.Config
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.MemoryCache
open TestHelpers

[<Fact>]
let ``Test clearFailure deletes failure record`` () =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path

    // Create a failure record
    let failure =
        { LastFailure = DateTimeOffset.Now
          ConsecutiveFailures = 2
          Kind = FetchFailureKind.HttpError }

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
    recordFailureOfKind NullLogger.Instance tmp.Path HttpError

    let failure1 =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(1, failure1.ConsecutiveFailures)

    // Record second failure
    recordFailureOfKind NullLogger.Instance tmp.Path HttpError

    let failure2 =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(2, failure2.ConsecutiveFailures)

[<Fact>]
let ``Test get retry periods from failure file`` () =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path

    let failure1 =
        { LastFailure = DateTimeOffset.Now.AddMinutes -30.0
          ConsecutiveFailures = 1
          Kind = FetchFailureKind.HttpError }

    let json1 = JsonSerializer.Serialize failure1
    OsFile.writeAllText failurePath json1

    let result = nextRetry NullLogger.Instance tmp.Path

    match result with
    | Some d -> Assert.True(d > DateTimeOffset.Now, "Backoff period should not have passed yet")
    | None -> failwithf $"No .faillure file found at {failurePath}"

    let failure2 =
        { LastFailure = DateTimeOffset.Now.AddHours -2.0
          ConsecutiveFailures = 1
          Kind = FetchFailureKind.HttpError }

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
          ConsecutiveFailures = 3
          Kind = FetchFailureKind.HttpError }

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
let ``Test recordFailure resets count when failure kind switches`` () =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path

    recordFailureOfKind NullLogger.Instance tmp.Path HttpError
    recordFailureOfKind NullLogger.Instance tmp.Path HttpError

    let beforeSwitch =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(2, beforeSwitch.ConsecutiveFailures)

    recordFailureOfKind NullLogger.Instance tmp.Path Timeout

    let afterSwitch =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(1, afterSwitch.ConsecutiveFailures)
    Assert.Equal(FetchFailureKind.Timeout, afterSwitch.Kind)

[<Fact>]
let ``Test recordFailure resets count when switching from timeout to http error`` () =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path

    recordFailureOfKind NullLogger.Instance tmp.Path Timeout
    recordFailureOfKind NullLogger.Instance tmp.Path Timeout

    let beforeSwitch =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(2, beforeSwitch.ConsecutiveFailures)

    recordFailureOfKind NullLogger.Instance tmp.Path HttpError

    let afterSwitch =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(1, afterSwitch.ConsecutiveFailures)
    Assert.Equal(FetchFailureKind.HttpError, afterSwitch.Kind)

[<Fact>]
let ``recordFailure classifies HttpRequestTimedOut as a Timeout failure`` () =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path
    let uri = Uri "https://example.com/feed"

    recordFailure NullLogger.Instance tmp.Path (HttpRequestTimedOut(uri, TimeSpan.FromSeconds 5.0))

    let failure =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(FetchFailureKind.Timeout, failure.Kind)

[<Theory>]
[<InlineData("HttpRequestNonSuccessStatus")>]
[<InlineData("HttpException")>]
[<InlineData("InvalidRssFeedFormat")>]
let ``recordFailure classifies other DomainErrors as an HttpError failure`` (errorTag: string) =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path
    let uri = Uri "https://example.com/feed"

    let error =
        match errorTag with
        | "HttpRequestNonSuccessStatus" -> HttpRequestNonSuccessStatus(uri, HttpStatusCode.InternalServerError)
        | "HttpException" -> HttpException(uri, Exception "boom")
        | "InvalidRssFeedFormat" -> InvalidRssFeedFormat(uri, Exception "bad xml")
        | other -> failwith $"unexpected error tag: {other}"

    recordFailure NullLogger.Instance tmp.Path error

    let failure =
        JsonSerializer.Deserialize<FetchFailure>(OsFile.readAllText failurePath)

    Assert.Equal(FetchFailureKind.HttpError, failure.Kind)

[<Fact>]
let ``Test getTimeoutBackoffMinutes follows doubling pattern and caps`` () =
    Assert.Equal(5.0, getTimeoutBackoffMinutes 1)
    Assert.Equal(20.0, getTimeoutBackoffMinutes 3)
    Assert.Equal(120.0, getTimeoutBackoffMinutes 10)

[<Fact>]
let ``Test nextRetry uses short backoff for timeout failure`` () =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path

    let failure =
        { LastFailure = DateTimeOffset.Now.AddMinutes -3.0
          ConsecutiveFailures = 1
          Kind = FetchFailureKind.Timeout }

    OsFile.writeAllText failurePath (JsonSerializer.Serialize failure)

    match nextRetry NullLogger.Instance tmp.Path with
    | Some d -> Assert.True(d > DateTimeOffset.Now, "5 min timeout backoff should not have elapsed after 3 min")
    | None -> failwithf $"No failure file found at {failurePath}"

[<Fact>]
let ``Test nextRetry uses long backoff for http error failure`` () =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path

    let failure =
        { LastFailure = DateTimeOffset.Now.AddMinutes -30.0
          ConsecutiveFailures = 1
          Kind = FetchFailureKind.HttpError }

    OsFile.writeAllText failurePath (JsonSerializer.Serialize failure)

    match nextRetry NullLogger.Instance tmp.Path with
    | Some d -> Assert.True(d > DateTimeOffset.Now, "1 h http error backoff should not have elapsed after 30 min")
    | None -> failwithf $"No failure file found at {failurePath}"

[<Theory>]
// Pre-Kind file with no failure-kind field at all
[<InlineData("""{"LastFailure":"{TS}","ConsecutiveFailures":1}""")>]
// Pre-Kind file still carrying the old IsTimeout flag
[<InlineData("""{"LastFailure":"{TS}","ConsecutiveFailures":1,"IsTimeout":true}""")>]
let ``Test nextRetry falls back to long backoff for legacy file without Kind`` (template: string) =
    use tmp = new TempPath()
    let failurePath = failureFilePath tmp.Path

    let recentTimestamp = DateTimeOffset.Now.AddMinutes(-30.0).ToString("o")
    OsFile.writeAllText failurePath (template.Replace("{TS}", recentTimestamp))

    match nextRetry NullLogger.Instance tmp.Path with
    | Some d -> Assert.True(d > DateTimeOffset.Now, "Legacy file should fall back to long backoff")
    | None -> failwithf $"No failure file found at {failurePath}"

[<Fact>]
let ``Test convertUrlToFilename`` () =
    Assert.Equal(Filename "https_abc_com_test", convertUrlToValidFilename (Uri "https://abc.com/test"))

    Assert.Equal(
        Filename "https_abc_com_test_rss_blabla",
        convertUrlToValidFilename (Uri "https://abc.com/test?rss=blabla")
    )

// computeBackoffState: cacheModified = when a (possibly stale) cache file was written (None = no cache),
// nextAttempt = next allowed retry from the failure record (None = no failure record).

[<Fact>]
let ``computeBackoffState: no failure record is ready to fetch`` () =
    let failureRecord = None
    Assert.Equal(ReadyToFetch, computeBackoffState None failureRecord)
    Assert.Equal(ReadyToFetch, computeBackoffState (Some(DateTimeOffset.Now.AddHours -2.0)) failureRecord)

[<Fact>]
let ``computeBackoffState: elapsed backoff is ready to fetch`` () =
    let nextAttempt = Some(DateTimeOffset.Now.AddHours -1.0)
    Assert.Equal(ReadyToFetch, computeBackoffState None nextAttempt)
    Assert.Equal(ReadyToFetch, computeBackoffState (Some(DateTimeOffset.Now.AddHours -5.0)) nextAttempt)

[<Fact>]
let ``computeBackoffState: active backoff with a cache reports the wait time`` () =
    let cacheModified = Some(DateTimeOffset.Now.AddMinutes -30.0)
    let nextAttempt = Some(DateTimeOffset.Now.AddHours 2.0)

    match computeBackoffState cacheModified nextAttempt with
    | InBackoffWithCache waitTime -> Assert.True(abs (waitTime.TotalHours - 2.0) < 0.1)
    | other -> Assert.Fail $"expected InBackoffWithCache, got {other}"

[<Fact>]
let ``computeBackoffState: active backoff without a cache reports the wait time`` () =
    let nextAttempt = Some(DateTimeOffset.Now.AddHours 2.0)

    match computeBackoffState None nextAttempt with
    | InBackoffNoCache waitTime -> Assert.True(abs (waitTime.TotalHours - 2.0) < 0.1)
    | other -> Assert.Fail $"expected InBackoffNoCache, got {other}"

let private cacheConfigIn (dir: OsPath) =
    { Dir = dir
      Expiration = TimeSpan.FromHours 1.0 }

[<Fact>]
let ``getCacheAge: missing cache file yields a PendingFetch with no modification time`` () =
    use tmp = new TempDir()
    let uri = Uri "https://example.com/feed"

    match getCacheAge NullLogger.Instance (cacheConfigIn tmp.Path) uri with
    | Some(PendingFetch(None, u)) -> Assert.Equal(uri, u)
    | other -> Assert.Fail $"expected Some (PendingFetch (None, _)), got {other}"

[<Fact>]
let ``getCacheAge: expired cache file yields a PendingFetch with its modification time`` () =
    use tmp = new TempDir()
    let uri = Uri "https://example.com/feed"
    let cachePath = cachePathFor (cacheConfigIn tmp.Path) uri
    writeCache cachePath "cached content"
    OsFile.setLastWriteTime cachePath (DateTime.Now.AddHours -5.0)

    match getCacheAge NullLogger.Instance (cacheConfigIn tmp.Path) uri with
    | Some(PendingFetch(Some _, u)) -> Assert.Equal(uri, u)
    | other -> Assert.Fail $"expected Some (PendingFetch (Some _, _)), got {other}"

[<Fact>]
let ``getCacheAge: fresh cache file yields None`` () =
    use tmp = new TempDir()
    let uri = Uri "https://example.com/feed"
    let cachePath = cachePathFor (cacheConfigIn tmp.Path) uri
    writeCache cachePath "cached content"

    Assert.Equal(None, getCacheAge NullLogger.Instance (cacheConfigIn tmp.Path) uri)

[<Fact>]
let ``applyBackoff: PendingFetch without a failure record is left untouched`` () =
    use tmp = new TempDir()
    let uri = Uri "https://example.com/feed"
    let ups = PendingFetch(None, uri)

    Assert.Equal(ups, checkIfInBackoff NullLogger.Instance (cacheConfigIn tmp.Path) ups)

[<Fact>]
let ``applyBackoff: PendingFetch in backoff without a cache becomes PreviousHttpRequestFailed`` () =
    use tmp = new TempDir()
    let uri = Uri "https://example.com/feed"
    let cachePath = OsPath.combine tmp.Path (convertUrlToValidFilename uri)
    recordFailureOfKind NullLogger.Instance cachePath HttpError

    match checkIfInBackoff NullLogger.Instance (cacheConfigIn tmp.Path) (PendingFetch(None, uri)) with
    | ProcessingError(PreviousHttpRequestFailed(u, _)) -> Assert.Equal(uri, u)
    | other -> Assert.Fail $"expected ProcessingError PreviousHttpRequestFailed, got {other}"

[<Fact>]
let ``applyBackoff: PendingFetch in backoff with a stale cache becomes PreviousHttpRequestFailedButPageCached`` () =
    use tmp = new TempDir()
    let uri = Uri "https://example.com/feed"
    let cachePath = OsPath.combine tmp.Path (convertUrlToValidFilename uri)
    recordFailureOfKind NullLogger.Instance cachePath HttpError
    let staleCacheModified = Some(DateTimeOffset.Now.AddHours -5.0)

    match checkIfInBackoff NullLogger.Instance (cacheConfigIn tmp.Path) (PendingFetch(staleCacheModified, uri)) with
    | ProcessingError(PreviousHttpRequestFailedButPageCached(u, _)) -> Assert.Equal(uri, u)
    | other -> Assert.Fail $"expected ProcessingError PreviousHttpRequestFailedButPageCached, got {other}"

[<Fact>]
let ``applyBackoff: states other than PendingFetch pass through`` () =
    use tmp = new TempDir()
    let ups = FeedArticles []

    Assert.Equal(ups, checkIfInBackoff NullLogger.Instance (cacheConfigIn tmp.Path) ups)

[<Fact>]
let ``readFromCache: ProcessingError with no associated Uri (invalid hostname) passes through unchanged`` () =
    use tmp = new TempDir()
    let memCache = InMemoryCache NullLogger.Instance
    let ups = ProcessingError(InvalidUriHostname(InvalidUri.Create "invalid-url"))

    Assert.Equal(ups, tryReadFromCaches (cacheConfigIn tmp.Path) memCache ups)

[<Fact>]
let ``readFromCache: ProcessingError with a Uri and no stale cache passes through unchanged`` () =
    use tmp = new TempDir()
    let memCache = InMemoryCache NullLogger.Instance
    let uri = Uri "https://example.com/feed"
    let ups = ProcessingError(PreviousHttpRequestFailed(uri, TimeSpan.FromHours 1.0))

    Assert.Equal(ups, tryReadFromCaches (cacheConfigIn tmp.Path) memCache ups)

[<Fact>]
let ``readFromCache: ProcessingError with a Uri and a stale cache returns UnparsedStaleCachedContent`` () =
    use tmp = new TempDir()
    let memCache = InMemoryCache NullLogger.Instance
    let uri = Uri "https://example.com/feed"
    let error = PreviousHttpRequestFailed(uri, TimeSpan.FromHours 1.0)
    writeCache (cachePathFor (cacheConfigIn tmp.Path) uri) "<rss>stale</rss>"

    match tryReadFromCaches (cacheConfigIn tmp.Path) memCache (ProcessingError error) with
    | UnparsedStaleCachedContent(content, u, e) ->
        Assert.Equal("<rss>stale</rss>", content)
        Assert.Equal(uri, u)
        Assert.Equal(error, e)
    | other -> Assert.Fail $"expected UnparsedStaleCachedContent, got {other}"
