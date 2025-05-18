module SimpleRssServer.Request

open Microsoft.Extensions.Logging
open System
open System.IO
open System.Net
open System.Net.Http
open System.Reflection
open System.Text
open System.Web

open RssParser

open SimpleRssServer.Helper
open SimpleRssServer.Logging
open System.Globalization


let convertUrlToValidFilename (url: string) : string =
    let replaceInvalidFilenameChars = RegularExpressions.Regex "[.?=:/]+"
    replaceInvalidFilenameChars.Replace(url, "_")

let getRssUrls (context: string) : string list =
    context
    |> HttpUtility.ParseQueryString
    |> fun query ->
        let rssValues = query.GetValues "rss"

        if rssValues <> null && rssValues.Length > 0 then
            rssValues |> List.ofArray
        else
            []

// Fetch the contents of a web page
let fetchUrlAsync (client: HttpClient) (url: string) (lastModified: DateTimeOffset option) (timeoutSeconds: float) =
    async {
        try
            use cts = new Threading.CancellationTokenSource(TimeSpan.FromSeconds timeoutSeconds)

            let request = new HttpRequestMessage(HttpMethod.Get, url)
            let version = Assembly.GetExecutingAssembly().GetName().Version.ToString()
            request.Headers.UserAgent.ParseAdd $"rssrdr/{version}"

            match lastModified with
            | Some date -> request.Headers.IfModifiedSince <- date
            | None -> ()

            let startTime = DateTimeOffset.Now
            let! response = client.SendAsync(request, cts.Token) |> Async.AwaitTask
            let endTime = DateTimeOffset.Now
            let duration = endTime - startTime
            logger.LogDebug $"Request to {url} took {duration.TotalMilliseconds} ms"

            if response.IsSuccessStatusCode then
                let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                return Success content
            else if response.StatusCode = HttpStatusCode.NotModified then
                return Success "No changes"
            else
                return Failure $"Failed to get {url}. Error: {response.StatusCode}."
        with
        | :? Threading.Tasks.TaskCanceledException ->
            return Failure $"Request to {url} timed out after {timeoutSeconds} seconds"
        | ex -> return Failure $"Failed to get {url}. {ex.GetType().Name}: {ex.Message}"
    }

let fetchUrlWithCacheAsync client (cacheLocation: string) (url: string) =
    async {
        let cacheFilename = convertUrlToValidFilename url
        let cachePath = Path.Combine(cacheLocation, cacheFilename)

        let fileExists = File.Exists cachePath

        let fileIsOld =
            if fileExists then
                let lastWriteTime = File.GetLastWriteTime cachePath |> DateTimeOffset
                (DateTimeOffset.Now - lastWriteTime).TotalHours > 1.0
            else
                false

        if not fileExists || fileIsOld then
            if fileIsOld then
                logger.LogDebug $"Cached file {cachePath} is older than 1 hour. Fetching {url}"
            else
                logger.LogInformation $"Did not find cached file {cachePath}. Fetching {url}"

            let lastModified =
                if fileExists then
                    File.GetLastWriteTime cachePath |> DateTimeOffset |> Some
                else
                    None

            let! page = fetchUrlAsync client url lastModified 5.0

            match page with
            | Success "No changes" ->
                try
                    logger.LogDebug $"Reading from cached file {cachePath}, because feed didn't change"
                    let! content = File.ReadAllTextAsync cachePath |> Async.AwaitTask
                    File.SetLastWriteTime(cachePath, DateTime.Now)
                    return Success content
                with ex ->
                    return Failure $"Failed to read file {cachePath}. {ex.GetType().Name}: {ex.Message}"
            | Success content ->
                File.WriteAllTextAsync(cachePath, content) |> ignore
                return page
            | Failure _ -> return page
        else
            logger.LogDebug $"Found cached file {cachePath} and it is up to date"
            let! content = File.ReadAllTextAsync cachePath |> Async.AwaitTask
            return Success content
    }

let fetchAllRssFeeds client (cacheLocation: string) (urls: string list) =
    urls
    |> List.map (fetchUrlWithCacheAsync client cacheLocation)
    |> Async.Parallel
    |> Async.RunSynchronously

let updateRequestLog (filename: string) (retention: TimeSpan) (urls: string list) =
    logger.LogDebug $"Updating request log {filename} with retention {retention.ToString()}"
    let currentDate = DateTime.Now

    let currentDateString =
        currentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let logEntries = urls |> List.map (fun url -> $"{currentDateString} {url}")

    let existingEntries =
        if File.Exists filename then
            File.ReadAllLines filename
            |> Array.toList
            |> List.filter (fun line ->
                let datePart = line.Split(' ').[0]

                let entryDate =
                    DateTime.ParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture)

                currentDate - entryDate <= retention)
        else
            []

    let updatedEntries = List.append existingEntries logEntries
    File.WriteAllLines(filename, updatedEntries)

let requestUrls logPath =
    if File.Exists logPath then
        File.ReadAllLines logPath
        |> Array.map (fun line -> line.Split(' ').[1])
        |> Array.distinct
        |> Array.toList
    else
        []

let convertArticleToHtml article =
    let date =
        if article.PostDate.IsSome then
            $"on %s{article.PostDate.Value.ToLongDateString()}"
        else
            ""

    $"""
    <div class="feed-item">
        <h2><a href="%s{article.Url}" target="_blank">%s{article.Title |> WebUtility.HtmlEncode}</a></h2>
        <div class="source-date">%s{article.BaseUrl} %s{date}</div>
        <p>%s{article.Text}</p>
    </div>
    """

let header = File.ReadAllText(Path.Combine("site", "header.html"))

let landingPage =
    header + File.ReadAllText(Path.Combine("site", "landing-page.html"))

let footer =
    """
    </body>
    </html>
    """

let homepage query rssItems =
    let body =
        $"""
    <body>
        <div class="header">
            <h1><a href="/" style="text-decoration: none; color: black;">rssrdr</a></h1>
            <a id="config-link" href="config.html/%s{query}">config/</a>
        </div>
    """

    let rssFeeds =
        rssItems
        |> Seq.collect parseRss
        |> Seq.sortByDescending (fun a -> a.PostDate)
        |> Seq.map convertArticleToHtml
        |> String.concat ""

    header + body + rssFeeds + footer

let configPage query =
    let body =
        """
    <body>
        <div class="header">
            <h1><a href="/" style="text-decoration: none; color: black;">rssrdr</a>/config</h1>
        </div>
    """

    let urlFields = getRssUrls query |> String.concat "\n"

    let textArea =
        $"""
        <form id='feed-form'>
            <label for='feeds'>Enter one feed URL per line:</label><br>
            <textarea id='feeds' rows='10' cols='30'>{urlFields}</textarea><br>
            <button type='button' onclick='submitFeeds()'>Submit</button>
        </form>
        """

    let filterFeeds =
        """
        <script>
            function submitFeeds() {
                const feeds = document.getElementById('feeds').value.trim().split('\n');
                const filteredFeeds = feeds.filter(feed => feed.trim() !== '');
                const queryString = filteredFeeds.map(feed => `rss=${encodeURIComponent(feed.trim())}`).join('&');
                window.location.href = `/?${queryString}`;
            }
        </script>
        """

    header + body + textArea + filterFeeds + footer

let notEmpty (s: string) = not (String.IsNullOrWhiteSpace s)

let assembleRssFeeds client cacheLocation rssUrls =
    let items = rssUrls |> List.filter notEmpty |> fetchAllRssFeeds client cacheLocation

    let rssQuery = rssUrls |> List.filter notEmpty |> String.concat "&rss="

    let query = if rssQuery.Length > 0 then $"?rss={rssQuery}" else rssQuery

    homepage query items

let requestLogPath = "rss-cache/request-log.txt"
let requestLogRetention = TimeSpan.FromDays 7

let handleRequest client (cacheLocation: string) (context: HttpListenerContext) =
    async {
        logger.LogInformation $"Received request {context.Request.Url}"

        let responseString =
            match context.Request.RawUrl with
            | Prefix "/config.html" _ -> configPage context.Request.Url.Query
            | Prefix "/?rss=" _ ->
                let rssUrls = getRssUrls context.Request.Url.Query

                updateRequestLog requestLogPath requestLogRetention rssUrls

                assembleRssFeeds client cacheLocation rssUrls
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
