module SimpleRssServer.HttpClient

open System
open System.Net
open System.Net.Http
open System.Reflection
open Microsoft.Extensions.Logging

open SimpleRssServer.DomainModel

let private userAgent =
    let version = Assembly.GetExecutingAssembly().GetName().Version.ToString()
    $"rssrdr/{version}"

let fetchUrlAsync
    (client: HttpClient)
    (logger: ILogger)
    (uri: Uri)
    (lastModified: DateTimeOffset option)
    (timeout: TimeSpan)
    =
    async {
        try
            use cts = new Threading.CancellationTokenSource(timeout)

            let request = new HttpRequestMessage(HttpMethod.Get, uri)
            request.Headers.UserAgent.ParseAdd userAgent

            lastModified
            |> Option.iter (fun date -> request.Headers.IfModifiedSince <- date)

            let startTime = DateTimeOffset.Now
            let! response = client.SendAsync(request, cts.Token) |> Async.AwaitTask
            let endTime = DateTimeOffset.Now
            let duration = endTime - startTime
            logger.LogDebug $"Request to {uri} took {duration.TotalMilliseconds} ms"

            if response.IsSuccessStatusCode then
                let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                return Ok content
            else if response.StatusCode = HttpStatusCode.NotModified then
                return Ok "No changes"
            else
                logger.LogError $"Failed to get {uri}. Error: {response.StatusCode}."
                return Error(HttpRequestNonSuccessStatus(uri, response.StatusCode))
        with
        | :? Threading.Tasks.TaskCanceledException ->
            logger.LogWarning $"Request to {uri} timed out after {timeout.TotalSeconds} seconds"
            return Error(HttpRequestTimedOut(uri, timeout))
        | ex ->
            logger.LogError $"Failed to get {uri}. {ex.GetType().Name}: {ex.Message}"
            return Error(HttpException(uri, ex))
    }
