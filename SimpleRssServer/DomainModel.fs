namespace SimpleRssServer.DomainModel

open System

open SimpleRssServer.DomainPrimitiveTypes
open System.Net

type DomainMessage =
    // Uri errors
    | UriHostNameMustContainDot of InvalidUri._T
    | UriFormatException of InvalidUri._T * Exception

    // Rss parsing errors
    // TODO consider adding the URI to these errors as well, to be able to provide more specific error messages in the feed
    | InvalidRssFeedFormat of Exception

    // Http errors
    | HttpRequestFailed of Uri * TimeSpan
    | HttpRequestTimedOut of Uri * TimeSpan
    | HttpRequestNonSuccessStatus of Uri * HttpStatusCode
    | HttpException of Uri * Exception

    // Cache errors
    // TODO introduce a Path like type
    | CacheReadFailed of Uri * string
    | CacheReadFailedWithException of Uri * string * Exception
