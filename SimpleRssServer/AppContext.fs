module SimpleRssServer.AppContext

open System.Net.Http
open Microsoft.Extensions.Logging

open SimpleRssServer.Config
open SimpleRssServer.DomainPrimitiveTypes
open SimpleRssServer.MemoryCache

type AppContext =
    { Client: HttpClient
      Logger: ILogger
      CacheConfig: CacheConfig
      MemCache: InMemoryCache
      CollectionsDir: OsPath
      RequestLogPath: OsPath }
