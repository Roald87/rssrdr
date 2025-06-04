module SimpleRssServer.Tests.TestHelpers

open System.IO

let deleteFile filePath =
    if File.Exists filePath then
        File.Delete filePath
