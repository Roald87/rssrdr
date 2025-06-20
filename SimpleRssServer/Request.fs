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

let convertUrlToValidFilename (uri: Uri) : string =
    let replaceInvalidFilenameChars = RegularExpressions.Regex "[.?=:/]+"
    replaceInvalidFilenameChars.Replace(uri.AbsoluteUri, "_")

let getRssUrls (context: string) : Uri list =
    context
    |> HttpUtility.ParseQueryString
    |> fun query ->
        let rssValues = query.GetValues "rss"

        if rssValues <> null && rssValues.Length > 0 then
            rssValues |> Array.map Uri |> List.ofArray
        else
            []

let fetchUrlWithCacheAsync client (cacheLocation: string) (uri: Uri) =
    async {
        let cacheFilename = convertUrlToValidFilename uri
        let cachePath = Path.Combine(cacheLocation, cacheFilename)

        let fileExists = File.Exists cachePath
        let fileIsOld = isCacheOld cachePath 1.0

        if not fileExists || fileIsOld then
            if fileIsOld then
                logger.LogDebug $"Cached file {cachePath} is older than 1 hour. Fetching {uri}"
            else
                logger.LogInformation $"Did not find cached file {cachePath}. Fetching {uri}"

            let lastModified =
                if fileExists then
                    File.GetLastWriteTime cachePath |> DateTimeOffset |> Some
                else
                    None

            let! page = fetchUrlAsync client logger uri lastModified RequestTimeout

            match page with
            | Success "No changes" ->
                try
                    logger.LogDebug $"Reading from cached file {cachePath}, because feed didn't change"
                    let! content = readCache cachePath
                    File.SetLastWriteTime(cachePath, DateTime.Now)
                    return Success content.Value
                with ex ->
                    return Failure $"Failed to read file {cachePath}. {ex.GetType().Name}: {ex.Message}"
            | Success content ->
                do! writeCache cachePath content
                return page
            | Failure _ -> return page
        else
            logger.LogDebug $"Found cached file {cachePath} and it is up to date"
            let! content = readCache cachePath
            return Success content.Value
    }

let fetchAllRssFeeds client (cacheLocation: string) (uris: Uri list) =
    uris
    |> List.map (fetchUrlWithCacheAsync client cacheLocation)
    |> Async.Parallel
    |> Async.RunSynchronously

let notEmpty (s: string) = not (String.IsNullOrWhiteSpace s)

let assembleRssFeeds client cacheLocation rssUris =
    let items = rssUris |> fetchAllRssFeeds client cacheLocation

    let rssQuery =
        rssUris
        |> List.map (fun u -> u.AbsoluteUri)
        |> List.filter notEmpty
        |> String.concat "&rss="

    let query = if rssQuery.Length > 0 then $"?rss={rssQuery}" else rssQuery
    homepage query items

let handleRequest client (cacheLocation: string) (context: HttpListenerContext) =
    async {
        logger.LogInformation $"Received request {context.Request.Url}"

        let rssUris = getRssUrls context.Request.Url.Query

        let responseString =
            match context.Request.RawUrl with
            | Prefix "/config.html" _ -> configPage rssUris
            | Prefix "/?rss=" _ ->
                updateRequestLog RequestLogPath RequestLogRetention rssUris
                assembleRssFeeds client cacheLocation rssUris
            | "/robots.txt" -> File.ReadAllText(Path.Combine("site", "robots.txt"))
            | "/sitemap.xml" -> File.ReadAllText(Path.Combine("site", "sitemap.xml"))
            | _ -> landingPage

        let buffer = responseString |> Encoding.UTF8.GetBytes
        context.Response.ContentLength64 <- int64 buffer.Length
        context.Response.ContentType <- "text/html"

        do!
            context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length)
            |> Async.AwaitTask

        context.Response.OutputStream.Close()
    }
