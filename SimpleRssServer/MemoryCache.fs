module SimpleRssServer.MemoryCache

open Microsoft.Extensions.Logging
open System

open SimpleRssServer.DomainModel

type private CacheMessage =
    | TryGet of uri: string * TimeSpan * AsyncReplyChannel<Article list option>
    | Set of uri: string * Article list

type InMemoryCache(logger: ILogger) =
    let agent =
        MailboxProcessor.Start(fun inbox ->
            let rec loop store =
                async {
                    match! inbox.Receive() with
                    | TryGet(feedUrl, expiration, reply) ->
                        match Map.tryFind feedUrl store with
                        | Some(articles, cachedAt) when DateTimeOffset.Now - cachedAt < expiration ->
                            logger.LogDebug $"Read articles of {feedUrl} from in-memory cache"
                            reply.Reply(Some articles)
                        | _ -> reply.Reply None

                        return! loop store
                    | Set(feedUrl, articles) -> return! loop (Map.add feedUrl (articles, DateTimeOffset.Now) store)
                }

            loop Map.empty)

    member _.TryGet(feedUrl: string, expiration: TimeSpan) : Article list option =
        agent.PostAndReply(fun reply -> TryGet(feedUrl, expiration, reply))

    member _.Set(feedUrl: string, articles: Article list) : unit = agent.Post(Set(feedUrl, articles))

let updateMemoryCache (memCache: InMemoryCache) (ups: UriProcessState) =
    match ups with
    | FeedArticles articles ->
        articles
        |> List.tryHead
        |> Option.iter (fun a -> memCache.Set(a.FeedUrl, articles))
    | _ -> ()

    ups
