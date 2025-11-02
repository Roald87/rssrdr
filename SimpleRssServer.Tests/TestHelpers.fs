module SimpleRssServer.Tests.TestHelpers

open System
open System.IO

open SimpleRssServer.Config

let deleteFile filePath =
    if File.Exists filePath then
        File.Delete filePath

let createOutdatedCache (cacheConfig: CacheConfig) (cachePath: string) (content: string) =
    File.WriteAllText(cachePath, content)
    let cacheAge = DateTime.Now.AddHours -(2.0 * cacheConfig.ExpirationHours)
    File.SetLastWriteTime(cachePath, cacheAge)
