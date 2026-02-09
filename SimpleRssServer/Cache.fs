module SimpleRssServer.Cache

open System
open System.IO
open System.Text.Json

type FetchFailure =
    { LastFailure: DateTimeOffset
      ConsecutiveFailures: int }

let failureFilePath cachePath = cachePath + ".failures"

let getBackoffHours failures =
    // Exponential backoff: 1hr, 2hrs, 4hrs, 8hrs, max 24hrs
    min 24.0 (Math.Pow(2.0, float (failures - 1)))

let fileLastModified (path: string) =
    if File.Exists path then
        File.GetLastWriteTime path |> DateTimeOffset |> Some
    else
        None

let readCache (cachePath: string) =
    async {
        if File.Exists cachePath then
            let! content = File.ReadAllTextAsync cachePath |> Async.AwaitTask
            return Some content
        else
            return None
    }

let createDirectoryForPath (path: string) =
    let dir = Path.GetDirectoryName(path)

    if not (String.IsNullOrEmpty(dir)) then
        Directory.CreateDirectory(dir) |> ignore

let writeCache (cachePath: string) (content: string) =
    async {
        createDirectoryForPath cachePath
        do! File.WriteAllTextAsync(cachePath, content) |> Async.AwaitTask
    }

let clearFailure (cachePath: string) =
    async {
        let failurePath = failureFilePath cachePath

        if File.Exists(failurePath) then
            File.Delete(failurePath)
    }

let recordFailure (cachePath: string) =
    async {
        let failurePath = failureFilePath cachePath
        createDirectoryForPath failurePath

        let failure =
            if File.Exists(failurePath) then
                try
                    let json = File.ReadAllText(failurePath)
                    let existing = JsonSerializer.Deserialize<FetchFailure>(json)

                    { LastFailure = DateTimeOffset.Now
                      ConsecutiveFailures = existing.ConsecutiveFailures + 1 }
                with _ ->
                    { LastFailure = DateTimeOffset.Now
                      ConsecutiveFailures = 1 }
            else
                { LastFailure = DateTimeOffset.Now
                  ConsecutiveFailures = 1 }

        let json = JsonSerializer.Serialize(failure)
        do! File.WriteAllTextAsync(failurePath, json) |> Async.AwaitTask
    }

let readFailure (cachePath: string) =
    let path = failureFilePath cachePath

    if File.Exists(path) then
        try
            let json = File.ReadAllText(path)
            Some(JsonSerializer.Deserialize<FetchFailure>(json))
        with _ ->
            None
    else
        None

let nextRetry (cachePath: string) =
    match readFailure cachePath with
    | None -> None // No failures recorded or can't read failure file
    | Some failure ->
        let backoffHours = getBackoffHours failure.ConsecutiveFailures
        Some(failure.LastFailure.AddHours backoffHours)

let clearExpiredCache (cacheDir: string) (retention: TimeSpan) =
    async {
        if Directory.Exists cacheDir then
            let currentTime = DateTime.Now
            let files = Directory.GetFiles cacheDir

            for file in files do
                if not (file.EndsWith(".failures")) then
                    let lastModified = File.GetLastWriteTime file
                    let fileAge = currentTime - lastModified

                    if fileAge > retention then
                        File.Delete file
                        let failureFile = failureFilePath file

                        if File.Exists failureFile then
                            File.Delete failureFile
    }
