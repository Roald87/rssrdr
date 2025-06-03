module SimpleRssServer.CacheModule

open System

// Placeholder content for CacheModule
let isCacheOld (cacheDate: DateTime) =
    let currentDate = DateTime.Now
    (currentDate - cacheDate).TotalDays > 7.0
