module SimpleRssServer.Collections

open System
open System.Security.Cryptography
open System.Text.RegularExpressions

open SimpleRssServer.DomainPrimitiveTypes

let private toBase64Url (bytes: byte[]) =
    Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')

let generateCollectionId () : CollectionId =
    RandomNumberGenerator.GetBytes 8 |> toBase64Url |> CollectionId

let isValidCollectionId (CollectionId collectionId) =
    Regex.IsMatch(collectionId, @"^[A-Za-z0-9_-]{6,16}$")

let private collectionFilePath (dir: OsPath) (CollectionId collectionId) = OsPath.join dir (collectionId + ".txt")

let save (dir: OsPath) (collectionId: CollectionId) (feeds: string list) =
    OsFile.writeAllLines (collectionFilePath dir collectionId) feeds

let tryLoad (dir: OsPath) (collectionId: CollectionId) : string list option =
    let path = collectionFilePath dir collectionId

    if OsFile.exists path then
        OsFile.readAllLines path
        |> Array.toList
        |> List.filter (fun s -> s.Trim() <> "")
        |> Some
    else
        None

let touch (dir: OsPath) (collectionId: CollectionId) =
    OsFile.setLastWriteTime (collectionFilePath dir collectionId) DateTime.Now

let delete (dir: OsPath) (collectionId: CollectionId) =
    let path = collectionFilePath dir collectionId

    if OsFile.exists path then
        OsFile.delete path

let deleteInactive (dir: OsPath) (retention: TimeSpan) =
    if OsDirectory.exists dir then
        OsDirectory.getFiles dir
        |> Array.filter (fun (OsPath p) -> p.EndsWith ".txt")
        |> Array.filter (OsFile.isOlderThan retention)
        |> Array.iter OsFile.delete
