module SimpleRssServer.MemoryCache

open Microsoft.Extensions.Logging
open System
open System.Collections.Concurrent

open SimpleRssServer.DomainModel

type InMemoryCache(logger: ILogger) =
    let cache = ConcurrentDictionary<string, Article list * DateTimeOffset>()

    member _.TryGet(feedUrl: string, expiration: TimeSpan) : Article list option =
        match cache.TryGetValue feedUrl with
        | true, (articles, cachedAt) when DateTimeOffset.Now - cachedAt < expiration ->
            logger.LogDebug $"Read articles of {feedUrl} from in-memory cache"
            Some articles
        | _ -> None

    member _.Set(feedUrl: string, articles: Article list) =
        cache[feedUrl] <- (articles, DateTimeOffset.Now)

let updateMemoryCache (memCache: InMemoryCache) (ups: UriProcessState) =
    match ups with
    | FeedArticles articles ->
        articles
        |> List.tryHead
        |> Option.iter (fun a -> memCache.Set(a.FeedUrl, articles))
    | _ -> ()

    ups
