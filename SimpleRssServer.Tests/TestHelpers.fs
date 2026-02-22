module SimpleRssServer.Tests.TestHelpers

open System.IO

open SimpleRssServer.DomainPrimitiveTypes

let deleteFile (filePath: OsPath) =
    if File.Exists filePath then
        File.Delete filePath
