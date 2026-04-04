namespace SimpleRssServer.DomainModel

open System

open SimpleRssServer.DomainPrimitiveTypes
open System.Net
open Roald87.FeedReader

type FeedUri =
    | FeedUri of Uri

    member this.Uri =
        let (FeedUri u) = this
        u

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

type Article =
    { PostDate: DateTime option
      Title: string
      ArticleUrl: string
      FeedUrl: string
      Text: string }

type UnparsedXml =
    | UnparsedXml of string

    member this.Value =
        let (UnparsedXml x) = this
        x

type UriProcessState =
    | ValidUri of (DateTimeOffset option) * Uri
    | CachedFeed of string * Uri
    | Response of string * Uri
    | ResponseCanContainsFeeds of string * Uri
    | ParsedFeed of UnparsedXml * Feed
    | ParsedCachedFeed of Feed
    | StaleHitWithError of string * Uri * DomainMessage
    | ParsedStaleHit of Feed * DomainMessage
    | ProcessingError of DomainMessage
    | FeedArticles of Article array
    | FeedWithErrorArticles of Article array
