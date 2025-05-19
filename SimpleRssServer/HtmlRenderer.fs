module SimpleRssServer.HtmlRenderer

open System.Net
open RssParser
open System.IO

let convertArticleToHtml (article: Article) =
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

let configPage rssUrls =
    let body =
        """
    <body>
        <div class="header">
            <h1><a href="/" style="text-decoration: none; color: black;">rssrdr</a>/config</h1>
        </div>
    """

    let urlFields = rssUrls |> String.concat "\n"

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
