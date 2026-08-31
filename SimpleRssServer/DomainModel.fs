namespace SimpleRssServer.DomainModel

open System

open SimpleRssServer.DomainPrimitiveTypes
open System.Net
open Roald87.FeedReader

type DomainError =
    // Uri errors
    | InvalidUriHostname of InvalidUri
    | InvalidUriFormat of InvalidUri * Exception

    // Rss parsing errors
    | InvalidRssFeedFormat of Uri * Exception
    | NoRssFeedsFoundInPage of Uri

    // Http errors
    | PreviousHttpRequestFailed of Uri * retryIn: TimeSpan
    | PreviousHttpRequestFailedButPageCached of Uri * retryIn: TimeSpan
    | HttpRequestTimedOut of Uri * timeout: TimeSpan
    | HttpRequestNonSuccessStatus of Uri * HttpStatusCode
    | HttpException of Uri * Exception

type FetchResponse =
    | Content of string
    | NotModified

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
    | PendingFetch of ifModifiedSince: DateTimeOffset option * Uri
    | UnparsedCachedContent of string * Uri
    | UnparsedHttpResponse of string * Uri
    | NotRssContent of string * Uri
    | ParsedLiveFeed of UnparsedXml * Feed
    | ParsedCachedFeed of Feed
    | UnparsedStaleCachedContent of string * Uri * DomainError
    | ParsedStaleFeed of Feed * DomainError
    | ProcessingError of DomainError
    | FeedArticles of Article list
    | DegradedArticles of Article list

[<AutoOpen>]
module ActivePatterns =
    let (|MessageUri|) (msg: DomainError) =
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

    /// The feed's Uri, for errors that carry one. Invalid-uri errors carry only the
    /// raw (possibly unparsable) string the user entered, so they have none.
    let (|DomainErrorUri|_|) (msg: DomainError) =
        match msg with
        | InvalidUriHostname _
        | InvalidUriFormat _ -> None
        | PreviousHttpRequestFailed(uri, _) -> Some uri
        | PreviousHttpRequestFailedButPageCached(uri, _) -> Some uri
        | HttpRequestTimedOut(uri, _) -> Some uri
        | HttpRequestNonSuccessStatus(uri, _) -> Some uri
        | HttpException(uri, _) -> Some uri
        | InvalidRssFeedFormat(uri, _) -> Some uri
        | NoRssFeedsFoundInPage uri -> Some uri
