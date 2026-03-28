module SimpleRssServer.RssParser

open Microsoft.Extensions.Logging
open Roald87.FeedReader
open System

open SimpleRssServer.DomainModel
open SimpleRssServer.Helper
open SimpleRssServer.Request
open SimpleRssServer.Config

let stripHtml (input: string) : string =
    if String.IsNullOrWhiteSpace input then
        ""
    else
        let regex = Text.RegularExpressions.Regex "<.*?>"
        let noHtml = regex.Replace(input, "")
        let removeRepeatingSpaces = Text.RegularExpressions.Regex "\s+"

        noHtml.Replace("\n", " ").Replace("\r", "").Trim()
        |> fun s -> removeRepeatingSpaces.Replace(s, " ")

let createErrorArticle (errorType: DomainMessage) : Article =
    let link = errorType.Uri |> Option.defaultValue ""

    let text =
        match errorType with
        | CacheReadFailed(uri, cachePath) -> $"Failed to read cached file from {cachePath} for {uri}."
        | CacheReadFailedWithException(uri, cachePath, ex) ->
            $"Failed to read cached file from {cachePath} for {uri}. {ex.GetType().Name}: {ex.Message}"
        | InvalidUriHostname u ->
            $"Ensure that you're using a valid address for this RSS feed. Invalid URI: {u.Value}. Host name must contain a dot."
        | InvalidUriFormat(u, ex) ->
            $"Ensure that you're using a valid address for this RSS feed. Invalid URI format: {u.Value}. {ex.GetType().Name}: {ex.Message}"
        | PreviousHttpRequestFailed(uri, waitTime) ->
            $"The {uri.Host} RSS feed seems to be offline. Retrying in {waitTime.TotalHours:F1} hours."
        | PreviousHttpRequestFailedButPageCached(uri, waitTime) ->
            $"The {uri.Host} RSS feed seems to be offline. Retrying in {waitTime.TotalHours:F1} hours. There is a saved version of the feed, which may be outdated. It is shown below."
        | InvalidRssFeedFormat(uri, ex) ->
            $"Ensure you entered the correct RSS feed address, the format of this feed was not recognized. Invalid RSS feed format for {uri}. {ex.GetType().Name}: {ex.Message}"
        | HttpRequestTimedOut(uri, timeOut) ->
            $"The {uri.Host} RSS feed seems to be offline. You can retry at a later time. Request to {uri} timed out after {timeOut.TotalSeconds:F1} seconds."
        | HttpException(uri, ex) ->
            $"The {uri.Host} RSS feed seems to be offline. You can retry at a later time. Failed to get {uri}. {ex.GetType().Name}: {ex.Message}"
        | HttpRequestNonSuccessStatus(uri, status) ->
            $"The {uri.Host} RSS feed seems to be offline. You can retry at a later time. Failed to get {uri}. Error: {status}."

    { PostDate = Some DateTime.Now
      Title = "Error"
      ArticleUrl = link
      FeedUrl = link
      Text = text }

let tryParseFeed (logger: ILogger) (content: string) (uri: Uri) : Result<Feed, DomainMessage> =
    try
        let feed = FeedReader.ReadFromString content
        // Link in feed points to the base url of the website, not the link to the feed.
        // The link to the feed is also not always available.
        feed.Link <- uri.AbsoluteUri
        Ok feed
    with ex ->
        logger.LogError $"Invalid RSS feed format. {ex.GetType().Name}: {ex.Message}"
        Error(InvalidRssFeedFormat(uri, ex))

let private getPostDate (feed: Feed) (entry: FeedItem) =
    if entry.PublishingDate.HasValue then
        Some entry.PublishingDate.Value
    elif feed.Type = FeedType.Atom then
        let atomEntry = entry.SpecificItem :?> Feeds.AtomFeedItem
        Option.ofNullable atomEntry.UpdatedDate
    else
        None

let private getArticleText (entry: FeedItem) =
    let content =
        if isText entry.Description then entry.Description
        elif isText entry.Content then entry.Content
        else ""

    let cleaned = content |> stripHtml

    if cleaned.Length > ArticleDescriptionLength then
        cleaned[.. ArticleDescriptionLength - 1] + "..."
    else
        cleaned

let parseFeedItems (feed: Feed) =
    feed.Items
    |> Seq.map (fun entry ->
        { PostDate = getPostDate feed entry
          Title = entry.Title
          ArticleUrl = entry.Link
          FeedUrl = feed.Link
          Text = getArticleText entry })
    |> Seq.toArray


let feedToArticles (ups: UriProcessState) : UriProcessState =
    match ups with 
    | ParsedFeed (_, feed) -> parseFeedItems feed |> FeedArticles
    | ProcessingError err -> [| createErrorArticle err |] |> FeedArticles
    | x -> x

// let parseRss (logger: ILogger) (fetchResult: FetchResult) : Article list =
//     match fetchResult with
//     | FreshContent(content, uri) ->
//         match tryParseFeed logger content uri with
//         | Ok feed -> feedToArticles feed
//         | Error err -> [ createErrorArticle err ]
//     | CachedContent(content, warning) ->
//         let errorArticle = createErrorArticle warning

//         let feedArticles =
//             warning.Uri
//             |> Option.map Uri
//             |> Option.bind (fun uri -> tryParseFeed logger content uri |> Result.toOption |> Option.map feedToArticles)
//             |> Option.defaultValue []

//         feedArticles @ [ errorArticle ]
//     | Failed e -> [ createErrorArticle e ]

// let parseFeedResult (logger: ILogger) (cacheConfig: CacheConfig) (fetchResult: FetchResult) =
//     match fetchResult with
//     | FreshContent(content, uri) ->
//         let feedUri = FeedUri uri

//         match tryParseFeed logger content uri with
//         | Ok feed ->
//             cacheSuccessfulFetch cacheConfig feedUri content
//             Ok(feedUri, feedToArticles feed)
//         | Error err -> Error [ createErrorArticle err ]
//     | other -> Error(parseRss logger other)
