module SimpleRssServer.Request

open Microsoft.Extensions.Logging
open System
open System.IO
open System.Text
open System.Web

open SimpleRssServer.Logging
open SimpleRssServer.HttpClient
open SimpleRssServer.Cache
open SimpleRssServer.Config

type Filename = Filename of string

type Path with
    static member Combine(path1: string, filename: Filename) =
        let (Filename s) = filename
        Path.Combine(path1, s)

let convertUrlToValidFilename (uri: Uri) =
    let replaceInvalidFilenameChars = RegularExpressions.Regex "[.?=:/]+"
    replaceInvalidFilenameChars.Replace(uri.AbsoluteUri, "_") |> Filename

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

let fetchAndReadPage client (uri: Uri) cacheModified cachePath =
    async {
        logger.LogDebug $"Fetching {uri}"
        let! page = fetchUrlAsync client logger uri cacheModified RequestTimeout

        match page with
        | Ok "No changes" ->
            try
                logger.LogDebug $"Reading from cached file {cachePath}, because feed didn't change"
                let! content = readCache cachePath
                File.SetLastWriteTime(cachePath, DateTime.Now)
                do! clearFailure cachePath
                return Ok content.Value
            with ex ->
                let errorMessage =
                    $"Failed to read file {cachePath}. {ex.GetType().Name}: {ex.Message}"

                logger.LogError errorMessage
                do! recordFailure cachePath
                return Error errorMessage
        | Ok content ->
            do! writeCache cachePath content
            do! clearFailure cachePath
            return page
        | Error _ ->
            do! recordFailure cachePath
            return page
    }

let fetchUrlWithCacheAsync client (cacheConfig: CacheConfig) (uri: Uri) =
    let cacheFilename = convertUrlToValidFilename uri
    let cachePath = Path.Combine(cacheConfig.Dir, cacheFilename)
    let cacheModified = fileLastModified cachePath

    let nextAttempt = nextRetry cachePath

    match cacheModified, nextAttempt with
    | None, None -> fetchAndReadPage client uri cacheModified cachePath
    | _, Some d when (d < DateTimeOffset.Now) -> fetchAndReadPage client uri cacheModified cachePath
    | Some d, None when (DateTimeOffset.Now - d) > cacheConfig.Expiration ->
        fetchAndReadPage client uri cacheModified cachePath
    | _, Some d ->
        let waitTime = (d - DateTimeOffset.Now).TotalHours
        async { return Error $"Previous request(s) to {uri} failed. You can retry in {waitTime:F1} hours." }
    | Some d, None ->
        async {
            let! cache = readCache cachePath

            match cache with
            | Some page -> return Ok page
            | None -> return Error "Something went wrong with reading the page of {uri} from cache."
        }

let fetchAllRssFeeds client (cacheConfig: CacheConfig) (uris: Uri array) =
    uris
    |> Array.map (fetchUrlWithCacheAsync client cacheConfig)
    |> Async.Parallel
    |> Async.RunSynchronously

let notEmpty (s: string) = not (String.IsNullOrWhiteSpace s)
