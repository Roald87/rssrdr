module SimpleRssServer.RssParser

open System
open System.IO

open Roald87.FeedReader

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

let createErrorFeed errorMessage =
    let feedItem = new FeedItem()
    feedItem.Title <- "Error"
    feedItem.PublishingDate <- Nullable DateTime.Now
    feedItem.Description <- errorMessage
    feedItem.Link <- ""

    let customFeed = new Feed()
    customFeed.Items <- [| feedItem |]

    customFeed

let parseRss (feedContent: Result<string, string>) : Article list =
    let feed =
        match feedContent with
        | Ok content ->
            try
                FeedReader.ReadFromString content
            with ex ->
                createErrorFeed $"Invalid RSS feed format. {ex.GetType().Name}: {ex.Message}"
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
                if not (entry.Description |> String.IsNullOrEmpty) then
                    entry.Description
                else if not (entry.Content |> String.IsNullOrEmpty) then
                    entry.Content
                else
                    ""

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


let parseRssFromFile fileName =
    try
        let content = File.ReadAllText fileName |> Ok
        parseRss content
    with ex ->
        [ { PostDate = Some DateTime.Now
            Title = "Error"
            Url = fileName
            BaseUrl = fileName
            Text = $"{ex.GetType().Name} {ex.Message}" } ]
