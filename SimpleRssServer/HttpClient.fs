module SimpleRssServer.HttpClient

open System
open System.Net
open System.Net.Http
open System.Reflection
open Microsoft.Extensions.Logging

open SimpleRssServer.Helper

let fetchUrlAsync
    (client: HttpClient)
    (logger: ILogger)
    (url: string)
    (lastModified: DateTimeOffset option)
    (timeoutSeconds: float)
    =
    async {
        try
            use cts = new Threading.CancellationTokenSource(TimeSpan.FromSeconds timeoutSeconds)

            let request = new HttpRequestMessage(HttpMethod.Get, url)
            let version = Assembly.GetExecutingAssembly().GetName().Version.ToString()
            request.Headers.UserAgent.ParseAdd $"rssrdr/{version}"

            match lastModified with
            | Some date -> request.Headers.IfModifiedSince <- date
            | None -> ()

            let startTime = DateTimeOffset.Now
            let! response = client.SendAsync(request, cts.Token) |> Async.AwaitTask
            let endTime = DateTimeOffset.Now
            let duration = endTime - startTime
            logger.LogDebug $"Request to {url} took {duration.TotalMilliseconds} ms"

            if response.IsSuccessStatusCode then
                let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                return Success content
            else if response.StatusCode = HttpStatusCode.NotModified then
                return Success "No changes"
            else
                return Failure $"Failed to get {url}. Error: {response.StatusCode}."
        with
        | :? Threading.Tasks.TaskCanceledException ->
            return Failure $"Request to {url} timed out after {timeoutSeconds} seconds"
        | ex -> return Failure $"Failed to get {url}. {ex.GetType().Name}: {ex.Message}"
    }
