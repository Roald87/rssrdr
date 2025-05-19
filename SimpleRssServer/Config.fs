module SimpleRssServer.Config

let CachePath = "rss-cache"
let RequestTimeout = 5.0
let RequestLogPath = "rss-cache/request-log.txt"
let RequestLogRetention = System.TimeSpan.FromDays 7
