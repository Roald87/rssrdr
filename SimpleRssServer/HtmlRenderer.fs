module SimpleRssServer.HtmlRenderer

open System
open System.IO
open System.Net

open Helper
open RssParser
open DomainPrimitiveTypes
open SimpleRssServer.DomainModel

let convertArticleToHtml (article: Article) : Html =
    let date =
        if article.PostDate.IsSome then
            $"on %s{article.PostDate.Value.ToLongDateString()}"
        else
            ""

    $"""
    <div>
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

let homepage (query: Query) (rssItems: Article seq) : Html =
    let body =
        $"""
    <body>
        <div>
            <h1><a href="/">rssrdr</a></h1>
            <a href="config.html/%s{query |> string}">config/</a>
            <a href="/random%s{query |> string}" style="margin-left: 20px;">random/</a>
        </div>
    """
        |> Html

    let rssFeeds =
        rssItems
        |> Seq.sortByDescending (fun a -> a.PostDate)
        |> Seq.map convertArticleToHtml
        |> Seq.fold (+) Html.Empty

    header + body + rssFeeds + footer

let randomPage (query: Query) (rssItems: Article seq) : Html =
    let body =
        $"""
    <body>
        <div>
            <h1><a href="/" >rssrdr</a></h1>
            <a href="config.html/%s{query |> string}">config/</a>
            <a href="/%s{query |> string}" style="margin-left: 20px;">chronological/</a>
        </div>
    """
        |> Html

    let shuffledFeeds =
        rssItems
        |> Seq.toArray
        |> Array.randomShuffle
        |> Seq.map convertArticleToHtml
        |> Seq.fold (+) Html.Empty

    header + body + shuffledFeeds + footer

let private configBody: Html =
    """
    <body>
        <div>
            <h1><a href="/">rssrdr</a>/config</h1>
        </div>
    """
    |> Html

let private feedsForm (confirmedUris: string) (extras: Html) : Html =
    let enteredFeeds =
        $"""
        <form>
            <label for='feeds'>Enter one feed URL per line.
                You can ommit the <code>https://</code>, but add <code>http://</code> if needed.
            </label><br>
            <textarea id='feeds' rows='10' cols='30'>{confirmedUris}</textarea><br>
            %s{string extras}
            <button type='button' onclick='submitFeeds()'>Submit</button>
        </form>
        """
        |> Html

    let submitFeedLinks =
        """
        <script>
            function submitFeeds() {
                const feeds = document.getElementById('feeds').value.trim().split('\n');
                const filteredFeeds = feeds.filter(feed => feed.trim() !== '');
                const checked = Array.from(document.querySelectorAll('input[name="discovered"]:checked')).map(cb => cb.value);
                const allFeeds = filteredFeeds.concat(checked);
                const queryString = allFeeds.map(feed => `rss=${feed.trim()}`).join('&');
                window.location.href = `/?${queryString}`;
            }
        </script>
        """
        |> Html

    enteredFeeds + submitFeedLinks

let configPage (rssUrls: Result<Uri, UriError> array) : Html =
    let validRssUris =
        rssUrls
        |> validUris
        |> Array.map (fun u -> u.AbsoluteUri.Replace("https://", ""))
        |> String.concat "\n"

    let errorFields = rssUrls |> invalidUris |> String.concat "<br>"

    let invalidDiv =
        if errorFields <> "" then
            $"""
            <div class='invalid-uris'>{errorFields}</div>
            """
            |> Html
        else
            Html.Empty

    header + configBody + feedsForm validRssUris invalidDiv + footer

let feedDiscoveryPage (confirmedRss: Uri[]) (toSelect: DiscoveredFeed list) : Html =
    let confirmedUris =
        confirmedRss
        |> Array.map (fun u -> u.AbsoluteUri.Replace("https://", ""))
        |> String.concat "\n"

    let checkboxItems =
        toSelect
        |> List.map (fun feed ->
            $"<label><input type='checkbox' name='discovered' value='{feed.Url}'> {Uri.BaseUrl feed.Url}: <a href='{feed.Url}'>{WebUtility.HtmlEncode feed.Title}</a></label>")
        |> String.concat "\n"

    let extras =
        $"""
        Select feeds:</br>
        {checkboxItems}
        """
        |> Html

    header + configBody + feedsForm confirmedUris extras + footer
