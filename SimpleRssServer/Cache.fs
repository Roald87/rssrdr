module SimpleRssServer.Cache

open System
open System.IO

let isCacheOld (cachePath: string) (maxAgeHours: float) =
    if File.Exists cachePath then
        let lastWriteTime = File.GetLastWriteTime cachePath |> DateTimeOffset
        (DateTimeOffset.Now - lastWriteTime).TotalHours > maxAgeHours
    else
        false

let readCache (cachePath: string) =
    async {
        if File.Exists cachePath then
            let! content = File.ReadAllTextAsync cachePath |> Async.AwaitTask
            return Some content
        else
            return None
    }

let writeCache (cachePath: string) (content: string) =
    async { do! File.WriteAllTextAsync(cachePath, content) |> Async.AwaitTask }
