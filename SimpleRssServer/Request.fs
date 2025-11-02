module SimpleRssServer.Request

open Microsoft.Extensions.Logging
open System
open System.IO
open System.Net
open System.Text
open System.Web

open SimpleRssServer.Helper
open SimpleRssServer.Logging
open SimpleRssServer.HttpClient
open SimpleRssServer.Cache
open SimpleRssServer.HtmlRenderer
open SimpleRssServer.RequestLog
open SimpleRssServer.Config
open RssParser

let convertUrlToValidFilename (uri: Uri) : string =
    let replaceInvalidFilenameChars = RegularExpressions.Regex "[.?=:/]+"
    replaceInvalidFilenameChars.Replace(uri.AbsoluteUri, "_")

let getRssUrls (context: string) : Result<Uri, string> array =
    context
    |> HttpUtility.ParseQueryString
    |> fun query ->
        let rssValues = query.GetValues "rss"

        let ensureScheme (s: string) =
            if
                s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            then
                s
            else
                $"https://{s}"

        if rssValues <> null && rssValues.Length > 0 then
            rssValues
            |> Array.map (fun s ->
                let url = ensureScheme s

                try
                    let uri = Uri url

                    if uri.Host.Contains "." then
                        Ok uri
                    else
                        Error $"Invalid URI: '{s}' (Host must contain a dot)"
                with :? UriFormatException as ex ->
                    Error $"Invalid URI: '{s}' ({ex.Message})")
        else
            [||]

let fetchUrlWithCacheAsync client (cacheLocation: string) (uri: Uri) =
    async {
        let cacheFilename = convertUrlToValidFilename uri
        let cachePath = Path.Combine(cacheLocation, cacheFilename)

        let cacheModified = fileLastModifued cachePath

        let cacheOutdated =
            match cacheModified with
            | Some d -> (DateTimeOffset.Now - d).TotalHours > 1.0
            | None -> true

        let canRetry = shouldRetry cachePath

        if cacheOutdated && canRetry then
            logger.LogDebug $"Fetching {uri}"
            let! page = fetchUrlAsync client logger uri cacheModified RequestTimeout

            match page with
            | Ok "No changes" ->
                try
                    logger.LogDebug $"Reading from cached file {cachePath}, because feed didn't change"
                    let! content = readCache cachePath
                    File.SetLastWriteTime(cachePath, DateTime.Now)
                    return Ok content.Value
                with ex ->
                    let errorMessage =
                        $"Failed to read file {cachePath}. {ex.GetType().Name}: {ex.Message}"

                    logger.LogError errorMessage
                    do! recordFailure cachePath
                    return Error errorMessage
            | Ok content ->
                do! writeCache cachePath content
                return page
            | Error _ ->
                do! recordFailure cachePath
                return page
        else if cacheModified.IsSome then
            logger.LogDebug $"Found up-to-date cached file at {cachePath}."
            let! cache = readCache cachePath

            match cache with
            | Some page -> return Ok page
            | None -> return Error "Something went wrong with reading the page from cache."
        else
            return Error "State should not be reached under normal conditions."
    }

let fetchAllRssFeeds client (cacheLocation: string) (uris: Uri array) =
    uris
    |> Array.map (fetchUrlWithCacheAsync client cacheLocation)
    |> Async.Parallel
    |> Async.RunSynchronously

let notEmpty (s: string) = not (String.IsNullOrWhiteSpace s)

type FeedOrder =
    | Chronological
    | Random

let assembleRssFeeds (logger: ILogger) order client cacheLocation rssUris =
    let items = rssUris |> validUris |> fetchAllRssFeeds client cacheLocation

    let invalidUris: Result<string, string>[] =
        rssUris
        |> Array.choose (function
            | Error e -> Some(Error e)
            | Ok _ -> None)

    let allItems = Array.append items invalidUris |> Seq.collect (parseRss logger)

    let rssQuery =
        rssUris
        |> validUris
        |> Array.map (fun u -> u.AbsoluteUri.Replace("https://", ""))
        |> String.concat "&rss="

    let query = if rssQuery.Length > 0 then $"?rss={rssQuery}" else rssQuery

    match order with
    | Chronological -> homepage query allItems
    | Random -> randomPage query allItems

let handleRequest client (cacheLocation: string) (context: HttpListenerContext) =
    async {
        logger.LogDebug $"Received request {context.Request.Url}"

        let rssUris = getRssUrls context.Request.Url.Query

        let responseString =
            match context.Request.RawUrl with
            | Prefix "/config.html" _ -> configPage rssUris |> string
            | Prefix "/random?rss=" _ ->
                updateRequestLog RequestLogPath RequestLogRetention rssUris
                assembleRssFeeds logger Random client cacheLocation rssUris |> string
            | Prefix "/?rss=" _ ->
                updateRequestLog RequestLogPath RequestLogRetention rssUris
                assembleRssFeeds logger Chronological client cacheLocation rssUris |> string
            | "/robots.txt" -> File.ReadAllText(Path.Combine("site", "robots.txt"))
            | "/sitemap.xml" -> File.ReadAllText(Path.Combine("site", "sitemap.xml"))
            | _ -> landingPage |> string

        let buffer = responseString |> Encoding.UTF8.GetBytes
        context.Response.ContentLength64 <- int64 buffer.Length
        context.Response.ContentType <- "text/html"

        do!
            context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length)
            |> Async.AwaitTask

        context.Response.OutputStream.Close()
    }
