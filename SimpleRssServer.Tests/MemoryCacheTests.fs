module SimpleRssServer.Tests.MemoryCacheTests

open Microsoft.Extensions.Logging.Abstractions
open System
open Xunit

open SimpleRssServer.DomainModel
open SimpleRssServer.MemoryCache

let makeMemCache () = InMemoryCache NullLogger.Instance

let private makeArticles feedUrl =
    [| { PostDate = Some DateTime.Now
         Title = "Test"
         ArticleUrl = "https://example.com/1"
         FeedUrl = feedUrl
         Text = "text" } |]

[<Fact>]
let ``TryGet on empty cache returns None`` () =
    let cache = makeMemCache ()
    Assert.Equal(None, cache.TryGet("https://example.com/feed", TimeSpan.FromHours 1))

[<Fact>]
let ``TryGet within expiration returns articles`` () =
    let cache = makeMemCache ()
    let articles = makeArticles "https://example.com/feed"
    cache.Set("https://example.com/feed", articles)
    Assert.Equal(Some articles, cache.TryGet("https://example.com/feed", TimeSpan.FromHours 1))

[<Fact>]
let ``TryGet after expiration returns None`` () =
    let cache = makeMemCache ()
    let articles = makeArticles "https://example.com/feed"
    cache.Set("https://example.com/feed", articles)
    Assert.Equal(None, cache.TryGet("https://example.com/feed", TimeSpan.FromSeconds 0.0))

[<Fact>]
let ``Set overwrites existing entry`` () =
    let cache = makeMemCache ()
    let first = makeArticles "https://example.com/feed"

    let second =
        [| { PostDate = None
             Title = "New"
             ArticleUrl = ""
             FeedUrl = "https://example.com/feed"
             Text = "" } |]

    cache.Set("https://example.com/feed", first)
    cache.Set("https://example.com/feed", second)
    Assert.Equal(Some second, cache.TryGet("https://example.com/feed", TimeSpan.FromHours 1))
