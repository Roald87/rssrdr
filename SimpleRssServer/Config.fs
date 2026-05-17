module SimpleRssServer.Config

open System
open DomainPrimitiveTypes
open System.Reflection

type CacheConfig = { Dir: OsPath; Expiration: TimeSpan }

type FetchConfig =
    { MaxParallelism: int
      Timeout: TimeSpan }

let DefaultCacheConfig =
    { Dir = OsPath "rss-cache"
      Expiration = TimeSpan.FromHours 1.0 }

let UserFetchConfig =
    { MaxParallelism = Int32.MaxValue
      Timeout = TimeSpan.FromSeconds 5.0 }

let CacheRefreshFetchConfig =
    { MaxParallelism = 8
      Timeout = TimeSpan.FromSeconds 30.0 }

let RequestLogPath = OsPath "rss-cache/request-log.txt"
let RequestLogRetention = TimeSpan.FromDays 7.0
let CacheRetention = TimeSpan.FromDays 7.0

let ArticleDescriptionLength = 255

let version = Assembly.GetExecutingAssembly().GetName().Version.ToString()
