module SimpleRssServer.Collections

open System
open System.Security.Cryptography
open System.Text.RegularExpressions

open SimpleRssServer.DomainPrimitiveTypes

let private toBase64Url (bytes: byte[]) =
    Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')

let generateShortCode () : string =
    RandomNumberGenerator.GetBytes 8 |> toBase64Url

let isValidShortCode (code: string) =
    Regex.IsMatch(code, @"^[A-Za-z0-9_-]{6,16}$")

let private collectionFilePath (dir: OsPath) (code: string) = OsPath.join dir (code + ".txt")

let save (dir: OsPath) (code: string) (feeds: string list) =
    OsFile.writeAllLines (collectionFilePath dir code) feeds

let tryLoad (dir: OsPath) (code: string) : string list option =
    let path = collectionFilePath dir code

    if OsFile.exists path then
        OsFile.readAllLines path
        |> Array.toList
        |> List.filter (fun s -> s.Trim() <> "")
        |> Some
    else
        None

let touch (dir: OsPath) (code: string) =
    OsFile.setLastWriteTime (collectionFilePath dir code) DateTime.Now

let delete (dir: OsPath) (code: string) =
    let path = collectionFilePath dir code

    if OsFile.exists path then
        OsFile.delete path

let deleteInactive (dir: OsPath) (retention: TimeSpan) =
    if OsDirectory.exists dir then
        let cutoff = DateTime.Now - retention

        OsDirectory.getFiles dir
        |> Array.filter (fun (OsPath p) -> p.EndsWith ".txt")
        |> Array.filter (fun path -> OsFile.getLastWriteTime path < cutoff)
        |> Array.iter OsFile.delete
