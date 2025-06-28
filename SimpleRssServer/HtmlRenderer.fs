module SimpleRssServer.HtmlRenderer

open System
open System.IO
open System.Net

open RssParser
open Helper

// Html type to wrap HTML strings
// [<Struct>] is an F# attribute that makes a type a struct (value type) instead of a class (reference type). Not needed here, so removed.
type Html =
    | Html of string

    override this.ToString() = let (Html s) = this in s
    static member (+)(Html a, Html b) = Html(a + b)
    static member Empty = Html ""

let convertArticleToHtml (article: Article) : Html =
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
    |> Html

let header: Html = File.ReadAllText(Path.Combine("site", "header.html")) |> Html

let versionNumber =
    let version = Reflection.Assembly.GetExecutingAssembly().GetName().Version
    $"{version.Major}.{version.Minor}.{version.Build}"

let landingPage: Html =
    header
    + (File.ReadAllText(Path.Combine("site", "landing-page.html"))
       |> fun html -> html.Replace("{{version}}", versionNumber)
       |> Html)

let footer: Html =
    """
    </body>
    </html>
    """
    |> Html

let homepage query (rssItems: Article seq) : Html =
    let body =
        $"""
    <body>
        <div class="header">
            <h1><a href="/" style="text-decoration: none; color: black;">rssrdr</a></h1>
            <a id="config-link" href="config.html/%s{query}">config/</a>
        </div>
    """
        |> Html

    let rssFeeds =
        rssItems
        |> Seq.sortByDescending (fun a -> a.PostDate)
        |> Seq.map convertArticleToHtml
        |> Seq.fold (+) Html.Empty

    header + body + rssFeeds + footer

let configPage (rssUrls: Result<Uri, string> array) : Html =
    let body =
        """
    <body>
        <div class="header">
            <h1><a href="/" style="text-decoration: none; color: black;">rssrdr</a>/config</h1>
        </div>
    """
        |> Html

    let validRssUris =
        rssUrls
        |> validUris
        |> Array.map (fun u -> u.AbsoluteUri.Replace("https://", ""))
        |> String.concat "\n"

    let textArea =
        $"""
        <form id='feed-form'>
            <label for='feeds'>Enter one feed URL per line.
                You can ommit the <code>https://</code>, but add <code>http://</code> if needed.
            </label><br>
            <textarea id='feeds' rows='10' cols='30'>{validRssUris}</textarea><br>
            <button type='button' onclick='submitFeeds()'>Submit</button>
        </form>
        """
        |> Html

    let errorFields = rssUrls |> invalidUris |> String.concat "<br>"

    let invalidDiv =
        if errorFields <> "" then
            $"""
            <div id='invalid-uris'>{errorFields}</div>
            """
            |> Html
        else
            Html.Empty

    let filterFeeds =
        """
        <script>
            function submitFeeds() {
                const feeds = document.getElementById('feeds').value.trim().split('\n');
                const filteredFeeds = feeds.filter(feed => feed.trim() !== '');
                const queryString = filteredFeeds.map(feed => `rss=${feed.trim()}`).join('&');
                window.location.href = `/?${queryString}`;
            }
        </script>
        """
        |> Html

    header + body + textArea + invalidDiv + filterFeeds + footer
