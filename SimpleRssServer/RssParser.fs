module SimpleRssServer.RssParser

open Microsoft.Extensions.Logging
open Roald87.FeedReader
open System

open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.Helper
open Config

type Article =
    { PostDate: DateTime option
      Title: string
      Url: string
      BaseUrl: string
      Text: string }

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

    let baseUrl =
        try
            Uri(link).BaseUrl
        with _ ->
            ""

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
      Url = link
      BaseUrl = baseUrl
      Text = text }

let tryParseFeed (logger: ILogger) (content: string) (uri: Uri) : Result<Feed, DomainMessage> =
    try
        Ok(FeedReader.ReadFromString content)
    with ex ->
        logger.LogError $"Invalid RSS feed format. {ex.GetType().Name}: {ex.Message}"
        Error(InvalidRssFeedFormat(uri, ex))

let feedToArticles (feed: Feed) : Article list =
    feed.Items
    |> Seq.map (fun entry ->
        let postDate =
            if entry.PublishingDate.HasValue then
                Some entry.PublishingDate.Value
            else if feed.Type = FeedType.Atom then
                let atomEntry = entry.SpecificItem :?> Feeds.AtomFeedItem

                match atomEntry.UpdatedDate.HasValue with
                | false -> None
                | true -> Some atomEntry.UpdatedDate.Value
            else
                None

        let title = entry.Title
        let link = entry.Link

        let baseUrl =
            try
                Uri(link).BaseUrl
            with _ ->
                ""

        let text =
            let content =
                if isText entry.Description then entry.Description
                else if isText entry.Content then entry.Content
                else ""

            let cleanedContent = content |> stripHtml

            if cleanedContent.Length > ArticleDescriptionLength then
                cleanedContent.Substring(0, ArticleDescriptionLength) + "..."
            else
                cleanedContent

        { PostDate = postDate
          Title = title
          Url = link
          BaseUrl = baseUrl
          Text = text })
    |> Seq.toList

let parseRss (logger: ILogger) (fetchResult: FetchResult) : Article list =
    match fetchResult with
    | FreshContent(content, uri) ->
        match tryParseFeed logger content uri with
        | Ok feed -> feedToArticles feed
        | Error err -> [ createErrorArticle err ]
    | CachedContent(content, warning) ->
        let errorArticle = createErrorArticle warning

        match warning.Uri |> Option.map Uri with
        | Some uri ->
            match tryParseFeed logger content uri with
            | Ok feed -> feedToArticles feed @ [ errorArticle ]
            | Error _ -> [ errorArticle ]
        | None -> [ errorArticle ]
    | Failed e -> [ createErrorArticle e ]
