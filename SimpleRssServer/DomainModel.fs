namespace SimpleRssServer.DomainModel

open System

open SimpleRssServer.DomainPrimitiveTypes
open System.Net
open Roald87.FeedReader

type FeedUri = FeedUri of Uri

and DomainMessage =
    // Uri errors
    | InvalidUriHostname of InvalidUri
    | InvalidUriFormat of InvalidUri * Exception

    // Rss parsing errors
    | InvalidRssFeedFormat of Uri * Exception
    | NoRssFeedsFoundInPage of Uri

    // Http errors
    | PreviousHttpRequestFailed of Uri * TimeSpan
    | PreviousHttpRequestFailedButPageCached of Uri * TimeSpan
    | HttpRequestTimedOut of Uri * TimeSpan
    | HttpRequestNonSuccessStatus of Uri * HttpStatusCode
    | HttpException of Uri * Exception

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
    | TryFetchFromCache of Uri
    | PendingFetch of (DateTimeOffset option) * Uri
    | UnparsedCachedContent of string * Uri
    | UnparsedHttpResponse of string * Uri
    | NotRssContent of string * Uri
    | ParsedFeed of UnparsedXml * Feed
    | ParsedCachedFeed of Feed
    | StaleHitWithError of string * Uri * DomainMessage
    | ParsedStaleHit of Feed * DomainMessage
    | ProcessingError of DomainMessage
    | FeedArticles of Article list
    | FeedWithErrorArticles of Article list

[<AutoOpen>]
module ActivePatterns =
    let (|MessageUri|) (msg: DomainMessage) =
        match msg with
        | InvalidUriHostname invalid -> invalid.Value
        | InvalidUriFormat(invalid, _) -> invalid.Value
        | PreviousHttpRequestFailed(uri, _) -> uri.AbsoluteUri
        | PreviousHttpRequestFailedButPageCached(uri, _) -> uri.AbsoluteUri
        | HttpRequestTimedOut(uri, _) -> uri.AbsoluteUri
        | HttpRequestNonSuccessStatus(uri, _) -> uri.AbsoluteUri
        | HttpException(uri, _) -> uri.AbsoluteUri
        | InvalidRssFeedFormat(uri, _) -> uri.AbsoluteUri
        | NoRssFeedsFoundInPage uri -> uri.AbsoluteUri
