module SimpleRssServer.Tests.RequestTests

open System
open Xunit

open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.Request
open TestHelpers

[<Fact>]
let ``Test getRssUrls`` () =
    let result = getRssUrls "?rss=https://abs.com/test"

    Assert.Equal<Result<Uri, UriError> list>([ Ok(Uri "https://abs.com/test") ], result)

[<Fact>]
let ``Test getRssUrls with other key should return empty list`` () =
    let result = getRssUrls "?foo=example.com"

    Assert.Equal<Result<Uri, UriError> list>([], result)

[<Fact>]
let ``Test getRssUrls with two URLs`` () =
    let result = getRssUrls "?rss=https://abs.com/test1&rss=https://abs.com/test2"

    let expected = [ Ok(Uri "https://abs.com/test1"); Ok(Uri "https://abs.com/test2") ]

    Assert.Equal<Result<Uri, UriError> list>(expected, result)

[<Fact>]
let ``Test getRssUrls with empty string`` () =
    let result = getRssUrls ""

    Assert.Equal<Result<Uri, UriError> list>([], result)

[<Fact>]
let ``Test getRssUrls with invalid URL`` () =
    let result = getRssUrls "?rss=invalid-url"
    Assert.Equal(1, result.Length)

    match result.[0] with
    | Error(HostNameMustContainDot url) -> Assert.Contains("invalid-url", url.Value)
    | x -> failwithf $"Expected Error HostNameMustContainDot, but got {x}"

[<Fact>]
let ``Test getRssUrls with valid and invalid URLs`` () =
    let result = getRssUrls "?rss=invalid-url&rss=https://valid-url.com"
    Assert.Equal(2, result.Length)

    match result.[0] with
    | Error(HostNameMustContainDot url) -> Assert.Contains("invalid-url", url.Value)
    | x -> failwithf $"Expected Error HostNameMustContainDot, but got {x}"

    Assert.Equal(Uri "https://valid-url.com", getOk result.[1])

[<Fact>]
let ``Test getRssUrls adds https if missing`` () =
    let result = getRssUrls "?rss=example.com/feed&rss=http://example.com/feed2"

    let expected =
        [ Ok(Uri "https://example.com/feed"); Ok(Uri "http://example.com/feed2") ]

    Assert.Equal<Result<Uri, UriError> list>(expected, result)

// computeCacheAndBackoffState: cacheModified = when the cache file was written (None = no cache),
// nextAttempt = next allowed retry from backoff (None = no failure record).

let private oneHour = TimeSpan.FromHours 1.0

[<Fact>]
let ``computeCacheAndBackoffState: no cache and no failures`` () =
    Assert.Equal(NoCacheNoFailures, computeCacheAndBackoffState None None oneHour)

[<Fact>]
let ``computeCacheAndBackoffState: fresh cache and no failures is a cache hit`` () =
    let cacheModified = Some(DateTimeOffset.Now.AddMinutes -30.0)
    Assert.Equal(CacheHit, computeCacheAndBackoffState cacheModified None oneHour)

[<Fact>]
let ``computeCacheAndBackoffState: stale cache and no failures is expired`` () =
    let cacheModified = Some(DateTimeOffset.Now.AddHours -2.0)
    Assert.Equal(CacheExpired, computeCacheAndBackoffState cacheModified None oneHour)

[<Fact>]
let ``computeCacheAndBackoffState: elapsed backoff is ready to retry`` () =
    let nextAttempt = Some(DateTimeOffset.Now.AddHours -1.0)
    Assert.Equal(ReadyToRetry, computeCacheAndBackoffState None nextAttempt oneHour)

[<Fact>]
let ``computeCacheAndBackoffState: an elapsed backoff wins over a stale cache`` () =
    let cacheModified = Some(DateTimeOffset.Now.AddHours -5.0)
    let nextAttempt = Some(DateTimeOffset.Now.AddHours -1.0)
    Assert.Equal(ReadyToRetry, computeCacheAndBackoffState cacheModified nextAttempt oneHour)

[<Fact>]
let ``computeCacheAndBackoffState: active backoff with a cache reports the wait time`` () =
    let cacheModified = Some(DateTimeOffset.Now.AddMinutes -30.0)
    let nextAttempt = Some(DateTimeOffset.Now.AddHours 2.0)

    match computeCacheAndBackoffState cacheModified nextAttempt oneHour with
    | InBackoffWithCache waitTime -> Assert.True(abs (waitTime.TotalHours - 2.0) < 0.1)
    | other -> Assert.Fail $"expected InBackoffWithCache, got {other}"

[<Fact>]
let ``computeCacheAndBackoffState: active backoff without a cache reports the wait time`` () =
    let nextAttempt = Some(DateTimeOffset.Now.AddHours 2.0)

    match computeCacheAndBackoffState None nextAttempt oneHour with
    | InBackoffNoCache waitTime -> Assert.True(abs (waitTime.TotalHours - 2.0) < 0.1)
    | other -> Assert.Fail $"expected InBackoffNoCache, got {other}"
