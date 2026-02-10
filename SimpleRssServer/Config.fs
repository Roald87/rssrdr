module SimpleRssServer.Config

type CacheConfig = { Dir: string; ExpirationHours: float }

let DefaultCacheConfig =
    { Dir = "rss-cache"
      ExpirationHours = 1.0 }

let RequestTimeout = 5.0
let RequestLogPath = "rss-cache/request-log.txt"
let RequestLogRetention = System.TimeSpan.FromDays 7
let CacheRetention = System.TimeSpan.FromDays 7
