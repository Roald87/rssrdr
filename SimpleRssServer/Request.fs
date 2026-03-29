module SimpleRssServer.Request

open System
open System.IO
open System.Text

open SimpleRssServer.HttpClient
open SimpleRssServer.Cache
open SimpleRssServer.Config
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.DomainModel

let convertUrlToValidFilename (uri: Uri) =
    let replaceInvalidFilenameChars = RegularExpressions.Regex "[.?=:/]+"
    replaceInvalidFilenameChars.Replace(uri.AbsoluteUri, "_") |> Filename

let getRssUrls (query: string) : Result<Uri, UriError> array =
    Query.Create query
    |> fun query -> query.GetValues "rss"
    |> fun (rssUrls: string array) ->

        if not (isNull rssUrls) && rssUrls.Length > 0 then
            rssUrls |> Array.map Uri.CreateWithHttps
        else
            [||]

type CacheState =
    | NoCacheNoFailures
    | ReadyToRetry
    | CacheExpired
    | InBackoffWithCache of waitTime: TimeSpan
    | InBackoffNoCache of waitTime: TimeSpan
    | CacheHit

let computeCacheState
    (cacheModified: DateTimeOffset option)
    (nextAttempt: DateTimeOffset option)
    (expiration: TimeSpan)
    =
    match cacheModified, nextAttempt with
    | None, None -> NoCacheNoFailures
    | _, Some na when na < DateTimeOffset.Now -> ReadyToRetry
    | Some cm, None when (DateTimeOffset.Now - cm) > expiration -> CacheExpired
    | Some _, Some na -> InBackoffWithCache(TimeSpan.FromHours (na - DateTimeOffset.Now).TotalHours)
    | _, Some na -> InBackoffNoCache(TimeSpan.FromHours (na - DateTimeOffset.Now).TotalHours)
    | Some _, None -> CacheHit

let fetchAllRssFeeds client logger (cacheConfig: CacheConfig) (uris: UriProcessState array) =
    let validUris =
        uris
        |> Array.choose (function
            | ValidUri(dt, uri) -> Some(dt, uri)
            | _ -> None)

    let rest =
        uris
        |> Array.filter (function
            | ValidUri _ -> false
            | _ -> true)

    // TODO double async needed?
    async {
        let! results =
            validUris
            |> Array.map (fun (dt, uri) ->
                async {
                    let! r = fetchUrlAsync client logger uri dt RequestTimeout
                    return uri, r
                })
            |> Async.Parallel

        let processed =
            results
            |> Array.map (function
                | uri, Ok "No changes" ->
                    let cachePath = Path.Combine(cacheConfig.Dir, convertUrlToValidFilename uri)
                    File.SetLastWriteTime(cachePath, DateTime.Now)

                    // TODO there should not be any cache reading in this method, move up
                    match readCache cachePath with
                    | Some content -> CachedFeed(content, uri)
                    | None -> ProcessingError(CacheReadFailed(uri, cachePath))
                | uri, Ok content -> Response(content, uri)
                | _, Error e -> ProcessingError e)

        return Array.append processed rest
    }
