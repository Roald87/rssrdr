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

let fileLastModifued (path: string) =
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
        // Clear any failure record when we successfully write cache
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

let shouldRetry (cachePath: string) =
    if File.Exists(failureFilePath cachePath) then
        try
            let json = File.ReadAllText(failureFilePath cachePath)
            let failure = JsonSerializer.Deserialize<FetchFailure>(json)
            let backoffHours = getBackoffHours failure.ConsecutiveFailures
            let timeSinceFailure = (DateTimeOffset.Now - failure.LastFailure).TotalHours
            timeSinceFailure >= backoffHours
        with _ ->
            true // If we can't read the failure file, allow retry
    else
        true // No failures recorded, so yes we can retry
