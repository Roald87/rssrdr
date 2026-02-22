module SimpleRssServer.Cache

open System
open System.IO
open System.Text.Json
open SimpleRssServer.Logging
open Microsoft.Extensions.Logging
open DomainPrimitiveTypes

type FetchFailure =
    { LastFailure: DateTimeOffset
      ConsecutiveFailures: int }

let failureFilePath (cachePath: OsPath) = cachePath + ".failures"

let getBackoffHours failures =
    // Exponential backoff: 1hr, 2hrs, 4hrs, 8hrs, max 24hrs
    min 24.0 (Math.Pow(2.0, float (failures - 1)))

let fileLastModified (path: OsPath) =
    if File.Exists path then
        File.GetLastWriteTime path |> DateTimeOffset |> Some
    else
        None

let readCache (cachePath: OsPath) =
    async {
        if File.Exists cachePath then
            let! content = File.ReadAllTextAsync cachePath |> Async.AwaitTask
            return Some content
        else
            return None
    }

let createDirectoryForPath (path: OsPath) =
    let (OsPath dir) = Path.GetDirectoryName path

    if not (String.IsNullOrEmpty dir) then
        Directory.CreateDirectory dir |> ignore

let writeCache cachePath (content: string) =
    async {
        createDirectoryForPath cachePath
        do! File.WriteAllTextAsync(cachePath, content) |> Async.AwaitTask
    }

let clearFailure cachePath =
    async {
        let failurePath = failureFilePath cachePath

        if File.Exists failurePath then
            File.Delete failurePath
    }

let recordFailure cachePath =
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

let readFailure cachePath =
    let path = failureFilePath cachePath

    if File.Exists(path) then
        try
            let json = File.ReadAllText(path)
            Some(JsonSerializer.Deserialize<FetchFailure>(json))
        with _ ->
            None
    else
        None

let nextRetry cachePath =
    match readFailure cachePath with
    | None -> None // No failures recorded or can't read failure file
    | Some failure ->
        let backoffHours = getBackoffHours failure.ConsecutiveFailures
        Some(failure.LastFailure.AddHours backoffHours)

let clearExpiredCache (cacheDir: OsPath) (retention: TimeSpan) =
    if not (Directory.Exists cacheDir) then
        logger.LogWarning("Cache directory {Dir} does not exist", cacheDir)
    else
        let now = DateTime.Now

        Directory.GetFiles cacheDir
        |> Array.filter (fun f -> (now - File.GetLastWriteTime f) > retention)
        |> Array.iter File.Delete
