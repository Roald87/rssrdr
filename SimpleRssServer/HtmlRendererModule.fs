module SimpleRssServer.HtmlRendererModule

open System.Web
open System.Globalization
open System.Net
open SimpleRssServer.RssParser
open SimpleRssServer.Helper

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

let homepage header footer query rssItems =
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
