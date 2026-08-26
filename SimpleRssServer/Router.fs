module SimpleRssServer.Router

open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.Helper

/// A resolved request target. Parsing the HTTP method and raw URL into this DU keeps
/// the routing decision separate from the IO each handler performs, so the dispatch
/// table can be tested without an HttpListener.
type Route =
    | ConfigPage
    | ChronologicalFeeds
    | ShuffleFeeds
    | RobotsTxt
    | SitemapXml
    | CreateCollection
    | ViewCollection of CollectionId
    | ViewCollectionShuffle of CollectionId
    | UpdateCollection of CollectionId
    | LandingPage

/// Collection-id validation is deliberately not done here; an id that parses as
/// `ViewCollection`/`UpdateCollection` may still be rejected by the handler.
let parseRoute (method: string) (rawUrl: string) : Route =
    match rawUrl with
    | Prefix "/config.html" _ -> ConfigPage
    | Prefix "/shuffle?rss=" _ -> ShuffleFeeds
    | Prefix "/?rss=" _ -> ChronologicalFeeds
    | "/robots.txt" -> RobotsTxt
    | "/sitemap.xml" -> SitemapXml
    | "/s" when method = "POST" -> CreateCollection
    | CollectionShuffleId collectionId when method = "GET" -> ViewCollectionShuffle collectionId
    | CollectionIdPath collectionId when method = "GET" -> ViewCollection collectionId
    | CollectionIdPath collectionId when method = "POST" -> UpdateCollection collectionId
    | _ -> LandingPage
