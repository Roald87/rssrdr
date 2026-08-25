module SimpleRssServer.HtmlRenderer

open System
open System.IO
open System.Net

open Helper
open DomainPrimitiveTypes
open SimpleRssServer.DomainModel

let removeFromQuery (query: Query) (feedToRemove: string) : string =
    let normalizedFeedUrl = FeedUri.removeScheme feedToRemove

    let remaining =
        query.Value.GetValues "rss"
        |> Array.filter (fun u -> FeedUri.removeScheme u <> normalizedFeedUrl)

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
            title="Remove {feedUrl |> FeedUri.removeScheme} from your feed"
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
        <div class="source-date">%s{article.FeedUrl |> FeedUri.baseUrl} %s{date}
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
                if (document.getElementById('saveCollection')?.checked) {
                    const existingCode = document.getElementById('existingCode')?.value;
                    const form = document.createElement('form');
                    form.method = 'post';
                    form.action = existingCode ? `/s/${existingCode}` : '/s';
                    allFeeds.forEach(feed => {
                        const input = document.createElement('input');
                        input.type = 'hidden';
                        input.name = 'rss';
                        input.value = feed.trim();
                        form.appendChild(input);
                    });
                    document.body.appendChild(form);
                    form.submit();
                } else {
                    const queryString = allFeeds.map(feed => `rss=${feed.trim()}`).join('&');
                    window.location.href = `/?${queryString}`;
                }
            }
        </script>
        """
        |> Html

    enteredFeeds + submitFeedLinks

let landingPage: Html =
    head + aboveFeedInput + feedsForm "" Html.Empty + belowFeedInput

let private saveCollectionCheckbox (existingCollectionId: CollectionId option) : Html =
    let checkedAttr = if existingCollectionId.IsSome then " checked" else ""

    let hiddenCodeInput =
        match existingCollectionId with
        | Some collectionId ->
            $"""<input type="hidden" id="existingCode" value="%s{WebUtility.HtmlEncode(string collectionId)}">"""
        | None -> ""

    $"""
    <p>
        <label>
            <input type="checkbox" id="saveCollection"%s{checkedAttr}> Save feed collection<br>
            <small>Creates a short link for you to easily access, edit and share your feeds. &#9888;&#65039; Note that everyone with this link can edit your collection!</small>
        </label>
    </p>
    %s{hiddenCodeInput}
    """
    |> Html

let configPage (rssUrls: Result<Uri, UriError> list) (existingCollectionId: CollectionId option) : Html =
    let validRssUris =
        rssUrls
        |> validUris
        |> List.map (fun u -> u.AbsoluteUri.Replace("https://", ""))
        |> String.concat "\n"

    head
    + aboveFeedInput
    + feedsForm validRssUris (saveCollectionCheckbox existingCollectionId)
    + belowFeedInput

let footer =
    """
    </body>
    </html>
    """
    |> Html

let private articlesToHtml (query: Query) (articles: Article list) : Html =
    let deleteButtons =
        articles
        |> List.map _.FeedUrl
        |> List.distinct
        |> List.map (fun feedUrl -> feedUrl, deleteFeedButton query feedUrl)
        |> Map.ofList

    articles
    |> List.map (fun a -> convertArticleToHtml deleteButtons[a.FeedUrl] a)
    |> Html.Concat

let private loadingOverlay: Html =
    Html """<div id="loading"><div class="spinner"></div><span>Loading feeds…</span></div>"""

let private loadingHideStyle: Html =
    Html """<style>#loading{display:none}</style>"""

let private feedsPageShell (configHref: string) (altHref: string) (altLabel: string) : Html =
    $"""
    <body>
        %s{string loadingOverlay}
        <div>
            <h1><a href="%s{configHref}">rssrdr</a></h1>
            <a href="%s{configHref}">config/</a>
            <a href="%s{altHref}" style="margin-left: 20px;">%s{altLabel}</a>
        </div>
    """
    |> Html
    |> (+) head

let chronologicalFeedsPageShell (query: Query) : Html =
    let q = query |> string
    feedsPageShell $"/config.html/%s{q}" $"/shuffle%s{q}" "shuffle/"

let chronologicalFeedsPageContent (query: Query) (rssItems: Article list) : Html =
    (rssItems |> List.sortByDescending _.PostDate |> articlesToHtml query)
    + removeFeedScript
    + loadingHideStyle
    + footer

let shuffledFeedsPageShell (query: Query) : Html =
    let q = query |> string
    feedsPageShell $"/config.html/%s{q}" $"/%s{q}" "chronological/"

let shuffledFeedsPageContent (query: Query) (rssItems: Article list) : Html =
    (rssItems |> List.randomShuffle |> articlesToHtml query)
    + removeFeedScript
    + loadingHideStyle
    + footer

let metaRefreshContent (redirectUrl: string) : Html =
    Html $"""<meta http-equiv="refresh" content="0; url={redirectUrl}">"""
    + loadingHideStyle
    + footer

let chronologicalFeedsPage (query: Query) (rssItems: Article list) : Html =
    chronologicalFeedsPageShell query + chronologicalFeedsPageContent query rssItems

let shuffledFeedsPage (query: Query) (rssItems: Article list) : Html =
    shuffledFeedsPageShell query + shuffledFeedsPageContent query rssItems

let collectionFeedsPageShell (collectionId: CollectionId) : Html =
    let configHref = $"/config.html?s=%s{string collectionId}"
    feedsPageShell configHref $"/s/%s{string collectionId}/shuffle" "shuffle/"

let collectionShuffledPageShell (collectionId: CollectionId) : Html =
    let configHref = $"/config.html?s=%s{string collectionId}"
    feedsPageShell configHref $"/s/%s{string collectionId}" "chronological/"

let collectionNotFoundPage (collectionId: CollectionId) : Html =
    $"""
    <body>
        <h1><a href="/">rssrdr</a></h1>
        <p>Collection <code>%s{WebUtility.HtmlEncode(string collectionId)}</code> not found.</p>
    </body>
    </html>
    """
    |> Html
    |> fun body -> head + body
