namespace SimpleRssServer.DomainModel

open System

open SimpleRssServer.DomainPrimitiveTypes
open System.Net

type FeedUri =
    | FeedUri of Uri

    member this.Uri =
        let (FeedUri u) = this
        u

type FetchResult =
    | FreshContent of string * Uri
    | CachedContent of string * DomainMessage
    | Failed of DomainMessage

and DomainMessage =
    // Uri errors
    | InvalidUriHostname of InvalidUri
    | InvalidUriFormat of InvalidUri * Exception

    // Rss parsing errors
    | InvalidRssFeedFormat of Uri * Exception

    // Http errors
    | PreviousHttpRequestFailed of Uri * TimeSpan
    | PreviousHttpRequestFailedButPageCached of Uri * TimeSpan
    | HttpRequestTimedOut of Uri * TimeSpan
    | HttpRequestNonSuccessStatus of Uri * HttpStatusCode
    | HttpException of Uri * Exception

    // Cache errors
    | CacheReadFailed of Uri * OsPath
    | CacheReadFailedWithException of Uri * OsPath * Exception

    member this.Uri =
        match this with
        | InvalidUriHostname invalid -> Some invalid.Value
        | InvalidUriFormat(invalid, _) -> Some invalid.Value
        | PreviousHttpRequestFailed(uri, _) -> Some uri.AbsoluteUri
        | PreviousHttpRequestFailedButPageCached(uri, _) -> Some uri.AbsoluteUri
        | HttpRequestTimedOut(uri, _) -> Some uri.AbsoluteUri
        | HttpRequestNonSuccessStatus(uri, _) -> Some uri.AbsoluteUri
        | HttpException(uri, _) -> Some uri.AbsoluteUri
        | InvalidRssFeedFormat(uri, _) -> Some uri.AbsoluteUri
        | CacheReadFailed(uri, _) -> Some uri.AbsoluteUri
        | CacheReadFailedWithException(uri, _, _) -> Some uri.AbsoluteUri

type DiscoveredFeed = { Title: string; Url: string }
