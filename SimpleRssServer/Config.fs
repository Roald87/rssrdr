module SimpleRssServer.Config

open System
open DomainPrimitiveTypes

type CacheConfig = { Dir: OsPath; Expiration: TimeSpan }

let DefaultCacheConfig =
    { Dir = OsPath "rss-cache"
      Expiration = TimeSpan.FromHours 1.0 }

let RequestTimeout = TimeSpan.FromSeconds 5.0
let RequestLogPath = OsPath "rss-cache/request-log.txt"
let RequestLogRetention = TimeSpan.FromDays 7.0
let CacheRetention = TimeSpan.FromDays 7.0
