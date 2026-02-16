module SimpleRssServer.Config

open System

type CacheConfig = { Dir: string; Expiration: TimeSpan }

let DefaultCacheConfig =
    { Dir = "rss-cache"
      Expiration = TimeSpan.FromHours 1.0 }

let RequestTimeout = TimeSpan.FromSeconds 5.0
let RequestLogPath = "rss-cache/request-log.txt"
let RequestLogRetention = TimeSpan.FromDays 7.0
let CacheRetention = TimeSpan.FromDays 7.0
