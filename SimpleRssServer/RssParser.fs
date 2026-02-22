module SimpleRssServer.RssParser

open Microsoft.Extensions.Logging
open Roald87.FeedReader
open System
open System.IO

open Helper
open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes

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

let ARTICLE_DESCRIPTION_LENGTH = 255

let createErrorFeed errorType =
    let feedItem = new FeedItem()
    feedItem.Title <- "Error"
    feedItem.PublishingDate <- Nullable DateTime.Now
    feedItem.Link <- ""

    match errorType with
    | CacheReadFailed(uri, cachePath) ->
        feedItem.Description <- $"Failed to read cached file from {cachePath} for {uri}."
        feedItem.Link <- uri.AbsoluteUri
    | CacheReadFailedWithException(uri, cachePath, ex) ->
        feedItem.Description <-
            $"Failed to read cached file from {cachePath} for {uri}. {ex.GetType().Name}: {ex.Message}"

        feedItem.Link <- uri.AbsoluteUri
    | UriHostNameMustContainDot u ->
        feedItem.Description <-
            $"Ensure that you're using a valid address for this RSS feed. Invalid URI: {InvalidUri.value u}. Host name must contain a dot."

        feedItem.Link <- InvalidUri.value u
    | DomainMessage.UriFormatException(u, ex) ->
        feedItem.Description <-
            $"Ensure that you're using a valid address for this RSS feed. Invalid URI format: {InvalidUri.value u}. {ex.GetType().Name}: {ex.Message}"

        feedItem.Link <- InvalidUri.value u
    | HttpRequestFailed(uri, waitTime) ->
        feedItem.Description <-
            $"The {uri.Host} RSS feed seems to be offline. I'll retry in {waitTime.TotalHours:F1} hours."

        feedItem.Link <- uri.AbsoluteUri
    | InvalidRssFeedFormat ex ->
        feedItem.Description <-
            $"Ensure you entered the correct RSS feed address, I didn't recognize the format of this feed. Invalid RSS feed format. {ex.GetType().Name}: {ex.Message}"
    | HttpRequestTimedOut(uri, waitTime) ->
        feedItem.Description <-
            $"The {uri.Host} RSS feed seems to be offline. You can retry at a later time. Request to {uri} timed out after {waitTime.TotalHours:F1} seconds."

        feedItem.Link <- uri.AbsoluteUri
    | HttpException(uri, ex) ->
        feedItem.Description <-
            $"The {uri.Host} RSS feed seems to be offline. You can retry at a later time. Failed to get {uri}. {ex.GetType().Name}: {ex.Message}"

        feedItem.Link <- uri.AbsoluteUri
    | HttpRequestNonSuccessStatus(uri, status) ->
        feedItem.Description <-
            $"The {uri.Host} RSS feed seems to be offline. You can retry at a later time. Failed to get {uri}. Error: {status}."

        feedItem.Link <- uri.AbsoluteUri

    let customFeed = new Feed()
    customFeed.Items <- [| feedItem |]

    customFeed

let parseRss (logger: ILogger) (feedContent: Result<string, DomainMessage>) : Article list =
    let feed =
        match feedContent with
        | Ok content ->
            try
                FeedReader.ReadFromString content
            with ex ->
                logger.LogError $"Invalid RSS feed format. {ex.GetType().Name}: {ex.Message}"
                InvalidRssFeedFormat ex |> createErrorFeed
        | Error error -> createErrorFeed error

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
                let uri = Uri link
                uri.Host.Replace("www.", "")
            with _ ->
                ""

        let text =
            let content =
                if isText entry.Description then entry.Description
                else if isText entry.Content then entry.Content
                else ""

            let cleanedContent = content |> stripHtml

            if cleanedContent.Length > ARTICLE_DESCRIPTION_LENGTH then
                cleanedContent.Substring(0, ARTICLE_DESCRIPTION_LENGTH) + "..."
            else
                cleanedContent

        { PostDate = postDate
          Title = title
          Url = link
          BaseUrl = baseUrl
          Text = text })
    |> Seq.toList


let parseRssFromFile logger fileName =
    try
        let content = File.ReadAllText fileName |> Ok
        parseRss logger content
    with ex ->
        [ { PostDate = Some DateTime.Now
            Title = "Error"
            Url = fileName
            BaseUrl = fileName
            Text = $"{ex.GetType().Name} {ex.Message}" } ]
