# SantanderTest API

ASP.NET Core (.NET 10) Web API that proxies the public
[Hacker News Firebase API](https://github.com/HackerNews/API) and exposes a
single endpoint that returns the best `N` stories of a given type, sorted by
score (and then by recency).

The solution is split into two projects:

| Project | Purpose |
| ------- | ------- |
| [`SantanderTest.Api`](src/SantanderTest.Api/) | The Web API itself. |
| [`SantanderTest.Tests`](src/SantanderTest.Tests/) | xUnit v3 unit and integration tests. |

---

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Outbound internet access to `https://hacker-news.firebaseio.com/`
  (configurable, see below).

No database, no message broker, no external dependency beyond the public
Hacker News API.

---

## How to run

From the repository root:

```bash
# Restore and build everything
dotnet build

# Run the API
dotnet run --project src/SantanderTest.Api/SantanderTest.Api.csproj
```

By default the [`launchSettings.json`](src/SantanderTest.Api/Properties/launchSettings.json)
profile binds to:

- HTTP:  `http://localhost:5136`
- HTTPS: `https://localhost:7133`

Once the process is up, Swagger UI is served at the application root, e.g.
[http://localhost:5136/swagger](http://localhost:5136/swagger).

### Calling the endpoint

```http
GET /hackernews/{type}?count={count}
```

- `type` (path, required): one of the Hacker News list endpoints, without the
  `stories.json` suffix. Valid values are `top`, `best`, `new`, `ask`,
  `show`, `job`.
- `count` (query, optional, default `10`): how many stories to return after
  sorting. Valid values are `1` through `100`.

Examples:

```bash
curl http://localhost:5136/hackernews/best
curl http://localhost:5136/hackernews/top?count=20
```

A successful response is a JSON array of objects shaped like
[`HackerNewsResponse`](src/SantanderTest.Api/Responses/HackerNewsResponse.cs):

```json
[
  {
    "id": 12345,
    "title": "Some headline",
    "url": "https://example.com/article",
    "postedBy": "jdoe",
    "time": "2026-04-29T12:34:56+00:00",
    "score": 482,
    "type": "story",
    "commentCount": 137
  }
]
```

### Configuration

The upstream URL lives in [`appsettings.json`](src/SantanderTest.Api/appsettings.json)
and can be overridden through the standard ASP.NET configuration providers
(environment variables, command line, user secrets, etc.):

```json
{
  "HackerNews": {
    "BaseUrl": "https://hacker-news.firebaseio.com/",
    "MaxDegreeOfParallelism": 8
  }
}
```

The configuration is bound through `IOptions<HackerNewsConfig>` with
`ValidateDataAnnotations` and `ValidateOnStart`, so a missing or malformed
`BaseUrl` makes the application fail fast at startup instead of crashing on
the first request. `MaxDegreeOfParallelism` controls how many individual
story requests may be in flight at once, defaults to `Environment.ProcessorCount`,
and is validated to stay between `1` and `100`.

---

## How to run the tests

```bash
dotnet test
```

The test project is split in two:

- [`Services/HackerNewsServiceTests.cs`](src/SantanderTest.Tests/Services/HackerNewsServiceTests.cs) -
  unit tests for the service, using a stub `HttpMessageHandler` so no real
  network call is made.
- [`Controllers/HackerNewsControllerTests.cs`](src/SantanderTest.Tests/Controllers/HackerNewsControllerTests.cs) -
  integration tests built on top of `WebApplicationFactory<Program>`, also
  fed by a stubbed handler injected via `ConfigureHttpClientDefaults`.

Both share the test infrastructure under
[`Infrastructure/`](src/SantanderTest.Tests/Infrastructure/).

---

## Design heuristics behind the endpoint

The endpoint looks small, but several intentional decisions shape its
behavior. Each of the following bullets explains *what* I did and *why*.

### 1. One generic endpoint, parameterized by list type

Hacker News exposes six similar list endpoints (`topstories`, `beststories`,
`newstories`, `askstories`, `showstories`, `jobstories`). Instead of building
six controller actions, the controller takes the list type as a path
parameter and forwards it to the service, which composes the upstream URL
(`v0/{type}stories.json`). This keeps the controller surface area minimal
and lets new list types be supported without any code change.

### 2. Two-layer caching with `BitFaster.Caching`

Hacker News rate limits and individual story fetches are the dominant
latency cost. The service keeps two LRU caches
([`HackerNewsService.cs`](src/SantanderTest.Api/Services/HackerNewsService.cs)):

- A cache of *id lists* keyed by list type (`top`, `best`, ...).
- A cache of *story DTOs* keyed by id.

Both caches are built with the same parameters:

- `WithAtomicGetOrAdd()` - atomic factory invocation. If two concurrent
  requests miss the same key, only one upstream call is made; the rest
  await the same `Task`. This avoids the classic cache stampede.
- `WithCapacity(3_000)` - bounded memory. Hot ids stay, cold ones are
  evicted. 3,000 is comfortably larger than the Hacker News
  "front-page-ish" working set (the lists return at most ~500 ids each).
- `WithExpireAfterWrite(TimeSpan.FromMinutes(5))` - bounded staleness.
  Five minutes is a deliberate trade-off: short enough that score and
  comment count stay reasonably current, long enough that repeated calls
  to the endpoint cost essentially nothing.
- `StringComparer.OrdinalIgnoreCase` is used for the list-type cache so
  that `/hackernews/Top` and `/hackernews/top` share an entry.

Failures are *not* cached. The test
`GetStoriesByTypeAsync_WhenItemFetchFails_DoesNotCacheFailure` pins this
behavior down: a 500 from upstream throws, but a retry hits the network
again and succeeds.

### 3. Singleton service, factory-created HttpClient

`HackerNewsService` is registered as a singleton so that the caches live as
long as the process. The `HttpClient` it consumes is registered through
`AddHttpClient`, which keeps outbound HTTP setup centralized and easy to
customize with timeouts, base addresses or resilience policies.

### 4. Bounded concurrent fetch with channels

Once the list of ids is known, the service partitions the ids across a
bounded number of workers. Each worker fetches one story at a time, so the
maximum number of upstream item requests is capped by
`HackerNews:MaxDegreeOfParallelism` instead of growing with the size of the
Hacker News list.

Completed stories are written into a bounded `Channel<HackerNewsDto>`, and
the single reader consumes that stream as results arrive. The channel keeps
memory pressure predictable and gives the workers backpressure if the reader
falls behind.

While consuming the channel, the service keeps only the best `count` stories
in a `PriorityQueue<HackerNewsDto, NewsPriority>`. That avoids materializing
and sorting the full list when the caller only asked for a small page.

### 5. Sort by score, then by recency

Implemented by [`NewsPriority`](src/SantanderTest.Api/Objects/NewsPriority.cs):
highest `Score` first, ties broken by newest `Time`, then by highest `Id`.
This gives stable, deterministic output while matching the intuitive "best"
ordering: high karma, recent first.

### 6. Separate upstream DTO and outbound response

[`HackerNewsDto`](src/SantanderTest.Api/Dtos/HackerNewsDto.cs) mirrors the
shape returned by Hacker News and is purely an internal contract.
[`HackerNewsResponse`](src/SantanderTest.Api/Responses/HackerNewsResponse.cs)
is what clients see. Three deliberate translations happen at the boundary:

- `Time` (Unix seconds, `long`) becomes a real `DateTimeOffset`.
- `By` becomes `PostedBy` (clearer name).
- `Descendants` becomes `CommentCount` (the upstream name leaks an
  implementation detail of the Hacker News tree model).

This way, if Hacker News renames a field, only the DTO and the mapping
need to change; the public contract stays stable.

### 7. Cancellation propagation

Every async path takes a `CancellationToken` and forwards it to
`HttpClient`, JSON deserialization, channel reads/writes and worker
execution. The test
`GetStoriesByTypeAsync_PropagatesCancellation` enforces this: cancelling
the token before the call surfaces as `OperationCanceledException`. This
matters because a client that closes the connection while the API is
fetching a Hacker News list should not leave background item requests
running.

### 8. Centralized error handling

Unhandled exceptions are caught by
[`ExceptionHandler`](src/SantanderTest.Api/Handlers/ExceptionHandler.cs),
which logs the full exception server-side via a source-generated
`LoggerMessage` and returns a sanitized JSON body to the client with HTTP
500. No stack traces leak through the wire.

### 9. Response compression

Hacker News stories returned in batches of up to 100 produce JSON payloads
where gzip helps a lot. `UseResponseCompression` with
`CompressionLevel.Optimal` is enabled for both HTTP and HTTPS in
[`Program.cs`](src/SantanderTest.Api/Program.cs).

### 10. JSON conventions

Configured globally in `Program.cs`:

- `CamelCase` property names.
- `IgnoreCycles` reference handling.
- `WhenWritingNull` ignore condition, so `url` and `text` disappear from
  the payload when they are not present (Ask HN posts have `text` but no
  `url`; "story" posts are usually the opposite).

### 11. Tests cover behavior, not implementation

The service tests exercise the public method through a stubbed
`HttpMessageHandler` that mimics the upstream protocol, asserting on
ordering, count, request paths actually issued, cache reuse, failure
non-caching and cancellation. The controller tests do the same end-to-end
through `WebApplicationFactory<Program>`, which keeps the DI container,
middleware pipeline and JSON contract honest. Together they describe the
intended behavior of the endpoint without coupling to internals.
