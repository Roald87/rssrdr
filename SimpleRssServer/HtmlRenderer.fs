module SimpleRssServer.HtmlRenderer

open System
open System.IO
open System.Net

open Helper
open DomainPrimitiveTypes
open SimpleRssServer.DomainModel

let removeFromQuery (query: Query) (feedToRemove: string) : string =
    let normalizedFeedUrl = Uri.RemoveScheme feedToRemove

    let remaining =
        query.Value.GetValues "rss"
        |> Array.filter (fun u -> Uri.RemoveScheme u <> normalizedFeedUrl)

    if remaining.Length = 0 then
        "/"
    else
        "?" + (remaining |> Array.map (fun u -> $"rss={u}") |> String.concat "&")

let head: Html = File.ReadAllText(Path.Combine("site", "head.html")) |> Html

let private trashIcon: string =
    File.ReadAllText(Path.Combine("site", "trash-can.svg"))

let private deleteFeedButton (query: Query) (feedUrl: string) : Html =
    let removeUrl = removeFromQuery query feedUrl

    $"""<button class="remove-feed"
            title="Remove {feedUrl |> Uri.RemoveScheme} from your feed"
            onclick="removeFeed('{removeUrl}', '{feedUrl}')">{trashIcon}</button>"""
    |> Html

let convertArticleToHtml (deleteButton: Html) (article: Article) : Html =
    let date =
        if article.PostDate.IsSome then
            $"on %s{article.PostDate.Value.ToLongDateString()}"
        else
            ""

    $"""
    <div>
        <h2><a href="%s{article.ArticleUrl}" target="_blank">%s{article.Title |> WebUtility.HtmlEncode}</a></h2>
        <div class="source-date">%s{article.FeedUrl |> Uri.BaseUrl} %s{date}
            %s{string deleteButton}
        </div>
        <p>%s{article.Text}</p>
    </div>
    """
    |> Html

let versionNumber =
    let version = Reflection.Assembly.GetExecutingAssembly().GetName().Version
    $"{version.Major}.{version.Minor}.{version.Build}"

let private aboveFeedInput: Html =
    """
    <body>
    <div>
        <h1 class="h1">rssrdr</h1>
    </div>
    <p><i>The simplest RSS reader on the planet.</i></p>
    <p><a href="/?rss=https://roaldin.ch/feed.xml&rss=https://spectrum.ieee.org/feeds&rss=https://seths.blog/feed">For example</a>, or enter your feeds below.</p>
    <p>Want to see your feeds on other devices? Just copy and bookmark the url.</p>
    """
    |> Html

let belowFeedInput: Html =
    """
    <p><small><a href="https://github.com/Roald87/rssrdr">Source code</a> - v{{version}}</small></p>
    </body>
    </html>
    """
    |> fun html -> html.Replace("{{version}}", versionNumber)
    |> Html

let private removeFeedScript: Html =
    """
    <script>
        function removeFeed(newUrl, feedUrl) {
            if (confirm(`Are you sure you want to remove ${feedUrl}?`)) {
                window.location.href = newUrl;
            }
        }
    </script>
    """
    |> Html

let private feedsForm (confirmedUris: string) (extras: Html) : Html =
    let enteredFeeds =
        $"""
        <form>
            <textarea id='feeds' rows='10' cols='30' placeholder='example.com/feed1&#10;http://example.com/feed2&#10;example.com'>{confirmedUris}</textarea><br>
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

let landingPage: Html =
    head + aboveFeedInput + feedsForm "" Html.Empty + belowFeedInput

let configPage (rssUrls: Result<Uri, UriError> array) : Html =
    let validRssUris =
        rssUrls
        |> validUris
        |> Array.map (fun u -> u.AbsoluteUri.Replace("https://", ""))
        |> String.concat "\n"

    head + aboveFeedInput + feedsForm validRssUris Html.Empty + belowFeedInput

let footer =
    """
    </body>
    </html>
    """
    |> Html

let private buildDeleteButtons (query: Query) (rssItems: Article array) : Map<string, Html> =
    rssItems
    |> Array.map _.FeedUrl
    |> Array.distinct
    |> Array.map (fun feedUrl -> feedUrl, deleteFeedButton query feedUrl)
    |> Map.ofArray

let private articlesToHtml (deleteButtons: Map<string, Html>) (articles: Article array) : Html =
    articles
    |> Array.map (fun a -> convertArticleToHtml deleteButtons.[a.FeedUrl] a |> string)
    |> String.concat ""
    |> Html

let chronologicalFeedsPage (query: Query) (rssItems: Article array) : Html =
    let body =
        $"""
    <body>
        <div>
            <h1><a href="config.html/%s{query |> string}">rssrdr</a></h1>
            <a href="/config.html/%s{query |> string}">config/</a>
            <a href="/shuffle%s{query |> string}" style="margin-left: 20px;">shuffle/</a>
        </div>
    """
        |> Html

    let deleteButtons = buildDeleteButtons query rssItems

    let rssFeeds =
        rssItems |> Array.sortByDescending _.PostDate |> articlesToHtml deleteButtons

    head + body + rssFeeds + removeFeedScript + footer

let shuffledFeedsPage (query: Query) (rssItems: Article array) : Html =
    let body =
        $"""
    <body>
        <div>
            <h1><a href="config.html/%s{query |> string}">rssrdr</a></h1>
            <a href="/config.html/%s{query |> string}">config/</a>
            <a href="/%s{query |> string}" style="margin-left: 20px;">chronological/</a>
        </div>
    """
        |> Html

    let deleteButtons = buildDeleteButtons query rssItems

    let shuffledFeeds = rssItems |> Array.randomShuffle |> articlesToHtml deleteButtons

    head + body + shuffledFeeds + removeFeedScript + footer
