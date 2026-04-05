module SimpleRssServer.MemoryCache

open Microsoft.Extensions.Logging
open System
open System.Collections.Concurrent

open SimpleRssServer.DomainModel

type InMemoryCache(logger: ILogger) =
    let cache = ConcurrentDictionary<string, Article array * DateTimeOffset>()

    member _.TryGet(feedUrl: string, expiration: TimeSpan) : Article array option =
        match cache.TryGetValue feedUrl with
        | true, (articles, cachedAt) when DateTimeOffset.Now - cachedAt < expiration ->
            logger.LogDebug $"Read articles of {feedUrl} from in-memory cache"
            Some articles
        | _ -> None

    member _.Set(feedUrl: string, articles: Article array) =
        cache[feedUrl] <- (articles, DateTimeOffset.Now)

let updateMemoryCache (memCache: InMemoryCache) (ups: UriProcessState) =
    match ups with
    | FeedArticles articles ->
        articles
        |> Array.tryHead
        |> Option.iter (fun a -> memCache.Set(a.FeedUrl, articles))
    | _ -> ()

    ups
