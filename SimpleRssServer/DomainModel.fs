namespace SimpleRssServer.DomainModel

open System

open SimpleRssServer.DomainPrimitiveTypes
open System.Net

type DomainMessage =
    // Uri errors
    | UriHostNameMustContainDot of InvalidUri
    | UriFormatException of InvalidUri * Exception

    // Rss parsing errors
    // TODO consider adding the URI to these errors as well, to be able to provide more specific error messages in the feed
    | InvalidRssFeedFormat of Exception

    // Http errors
    | PreviousHttpRequestFailed of Uri * TimeSpan
    | PreviousHttpRequestFailedButPageCached of Uri * TimeSpan * string
    | HttpRequestTimedOut of Uri * TimeSpan
    | HttpRequestNonSuccessStatus of Uri * HttpStatusCode
    | HttpException of Uri * Exception

    // Cache errors
    | CacheReadFailed of Uri * OsPath
    | CacheReadFailedWithException of Uri * OsPath * Exception

    member this.Uri =
        match this with
        | UriHostNameMustContainDot invalid -> Some invalid.value
        | UriFormatException(invalid, _) -> Some invalid.value
        | PreviousHttpRequestFailed(uri, _) -> Some uri.AbsoluteUri
        | PreviousHttpRequestFailedButPageCached(uri, _, _) -> Some uri.AbsoluteUri
        | HttpRequestTimedOut(uri, _) -> Some uri.AbsoluteUri
        | HttpRequestNonSuccessStatus(uri, _) -> Some uri.AbsoluteUri
        | HttpException(uri, _) -> Some uri.AbsoluteUri
        | InvalidRssFeedFormat _ -> None
        | CacheReadFailed(uri, _) -> Some uri.AbsoluteUri
        | CacheReadFailedWithException(uri, _, _) -> Some uri.AbsoluteUri
