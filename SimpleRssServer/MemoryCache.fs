module SimpleRssServer.MemoryCache

open Microsoft.Extensions.Logging
open System

open SimpleRssServer.DomainModel

type private CacheMessage =
    | TryGet of uri: string * TimeSpan * AsyncReplyChannel<Article list option>
    | Set of uri: string * Article list

let private handleMessage (logger: ILogger) store message =
    match message with
    | TryGet(feedUrl, expiration, reply) ->
        match Map.tryFind feedUrl store with
        | Some(articles, cachedAt) when DateTimeOffset.Now - cachedAt < expiration ->
            logger.LogDebug $"Read articles of {feedUrl} from in-memory cache"
            reply.Reply(Some articles)
        | _ -> reply.Reply None

        store
    | Set(feedUrl, articles) -> Map.add feedUrl (articles, DateTimeOffset.Now) store

[<TailCall>]
let rec private loop (logger: ILogger) (inbox: MailboxProcessor<CacheMessage>) store =
    async {
        let! message = inbox.Receive()
        return! loop logger inbox (handleMessage logger store message)
    }

type InMemoryCache(logger: ILogger) =
    let agent = MailboxProcessor.Start(fun inbox -> loop logger inbox Map.empty)

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
