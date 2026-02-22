module SimpleRssServer.RssParser

open Microsoft.Extensions.Logging
open Roald87.FeedReader
open System

open SimpleRssServer.DomainModel
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.Helper

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
    let errorItem = new FeedItem()
    errorItem.Title <- "Error"
    errorItem.PublishingDate <- Nullable DateTime.Now
    errorItem.Link <- ""

    match errorType with
    | CacheReadFailed(uri, cachePath) ->
        errorItem.Description <- $"Failed to read cached file from {cachePath} for {uri}."
        errorItem.Link <- uri.AbsoluteUri
    | CacheReadFailedWithException(uri, cachePath, ex) ->
        errorItem.Description <-
            $"Failed to read cached file from {cachePath} for {uri}. {ex.GetType().Name}: {ex.Message}"

        errorItem.Link <- uri.AbsoluteUri
    | UriHostNameMustContainDot u ->
        errorItem.Description <-
            $"Ensure that you're using a valid address for this RSS feed. Invalid URI: {InvalidUri.value u}. Host name must contain a dot."

        errorItem.Link <- InvalidUri.value u
    | DomainMessage.UriFormatException(u, ex) ->
        errorItem.Description <-
            $"Ensure that you're using a valid address for this RSS feed. Invalid URI format: {InvalidUri.value u}. {ex.GetType().Name}: {ex.Message}"

        errorItem.Link <- InvalidUri.value u
    | PreviousHttpRequestFailed(uri, waitTime) ->
        errorItem.Description <-
            $"The {uri.Host} RSS feed seems to be offline. Rretring in {waitTime.TotalHours:F1} hours."

        errorItem.Link <- uri.AbsoluteUri
    | PreviousHttpRequestFailedButPageCached(uri, waitTime, _) ->
        errorItem.Description <-
            $"The {uri.Host} RSS feed seems to be offline. Retrying in {waitTime.TotalHours:F1} hours. There is a saved version of the feed, which may be outdated. It is shown below."

        errorItem.Link <- uri.AbsoluteUri
    | InvalidRssFeedFormat ex ->
        errorItem.Description <-
            $"Ensure you entered the correct RSS feed address, the format of this feed was not recognized. Invalid RSS feed format. {ex.GetType().Name}: {ex.Message}"
    | HttpRequestTimedOut(uri, timeOut) ->
        errorItem.Description <-
            $"The {uri.Host} RSS feed seems to be offline. You can retry at a later time. Request to {uri} timed out after {timeOut.TotalSeconds:F1} seconds."

        errorItem.Link <- uri.AbsoluteUri
    | HttpException(uri, ex) ->
        errorItem.Description <-
            $"The {uri.Host} RSS feed seems to be offline. You can retry at a later time. Failed to get {uri}. {ex.GetType().Name}: {ex.Message}"

        errorItem.Link <- uri.AbsoluteUri
    | HttpRequestNonSuccessStatus(uri, status) ->
        errorItem.Description <-
            $"The {uri.Host} RSS feed seems to be offline. You can retry at a later time. Failed to get {uri}. Error: {status}."

        errorItem.Link <- uri.AbsoluteUri

    let errorOnlyFeed = new Feed()
    errorOnlyFeed.Items <- [| errorItem |]

    errorOnlyFeed

let parseRss (logger: ILogger) (feedContent: Result<string, DomainMessage>) : Article list =
    let feed =
        match feedContent with
        | Ok content ->
            try
                FeedReader.ReadFromString content
            with ex ->
                logger.LogError $"Invalid RSS feed format. {ex.GetType().Name}: {ex.Message}"
                InvalidRssFeedFormat ex |> createErrorFeed
        | Error error ->
            let errorFeed = createErrorFeed error

            match feedContent with
            | Error(PreviousHttpRequestFailedButPageCached(_, _, cachedContent)) ->
                let cachedFeed = FeedReader.ReadFromString cachedContent
                cachedFeed.Items.Add errorFeed.Items.[0]
                cachedFeed
            | _ -> errorFeed

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
