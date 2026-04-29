using System.Net;
using EmagineTest.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SantanderTest.Api.Configs;
using SantanderTest.Api.Dtos;
using SantanderTest.Api.Services;

namespace EmagineTest.Tests.Services
{
    public sealed class HackerNewsServiceTests
    {
        private static readonly Uri BaseUrl = new("https://hacker-news.test/");
        private static readonly IComparer<HackerNewsDto> ScoreThenIdDesc = Comparer<HackerNewsDto>.Create((a, b) =>
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : b.Id.CompareTo(a.Id);
        });
        private static HackerNewsDto Story(int id, int score = 0, long time = 0L, string title = "t", string by = "u",
                                           string type = "story", int descendants = 0)
        {
            return new()
            {
                Id = id,
                Title = title,
                By = by,
                Time = time,
                Score = score,
                Type = type,
                Descendants = descendants,
            };
        }

        private sealed class TestHost : IDisposable
        {
            private readonly HttpClient _httpClient;
            public StubHttpMessageHandler Handler { get; }
            public HackerNewsService Service { get; }
            public TestHost(StubHttpMessageHandler handler)
            {
                Handler = handler;
                _httpClient = new(handler, disposeHandler: false);
                Service = new(
                    _httpClient,
                    Options.Create(new HackerNewsConfig { BaseUrl = BaseUrl }),
                    NullLogger<HackerNewsService>.Instance);
            }

            public void Dispose() => _httpClient.Dispose();
        }

        [Fact]
        public async Task GetStoriesByTypeAsync_OrdersByScoreDescending()
        {
            using StubHttpMessageHandler handler = new(req =>
            {
                return req.RequestUri!.AbsolutePath switch
                {
                    "/v0/topstories.json" => StubHttpMessageHandler.Json(new[] { 1, 2, 3 }),
                    "/v0/item/1.json" => StubHttpMessageHandler.Json(Story(1, score: 10)),
                    "/v0/item/2.json" => StubHttpMessageHandler.Json(Story(2, score: 50)),
                    "/v0/item/3.json" => StubHttpMessageHandler.Json(Story(3, score: 30)),
                    _ => new(HttpStatusCode.NotFound),
                };
            });
            using TestHost host = new(handler);
            var stories = await host.Service.GetStoriesByTypeAsync("top", 3, ScoreThenIdDesc, TestContext.Current.CancellationToken);
            Assert.Equal([2, 3, 1], stories.Select(s => s.Id));
        }

        [Fact]
        public async Task GetStoriesByTypeAsync_RespectsCountAfterSorting()
        {
            using StubHttpMessageHandler handler = new(req =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if(path == "/v0/topstories.json")
                {
                    return StubHttpMessageHandler.Json(Enumerable.Range(1, 5).ToArray());
                }
                if(path.StartsWith("/v0/item/", StringComparison.Ordinal))
                {
                    var idStr = path["/v0/item/".Length..^".json".Length];
                    var id = int.Parse(idStr, System.Globalization.CultureInfo.InvariantCulture);
                    return StubHttpMessageHandler.Json(Story(id, score: id * 10));
                }
                return new(HttpStatusCode.NotFound);
            });
            using TestHost host = new(handler);
            var stories = await host.Service.GetStoriesByTypeAsync("top", 2, ScoreThenIdDesc, TestContext.Current.CancellationToken);
            Assert.Equal([5, 4], stories.Select(s => s.Id));
        }

        [Fact]
        public async Task GetStoriesByTypeAsync_FetchesIdsListAndEachStoryById()
        {
            using StubHttpMessageHandler handler = new(req =>
            {
                return req.RequestUri!.AbsolutePath switch
                {
                    "/v0/beststories.json" => StubHttpMessageHandler.Json(new[] { 7, 9 }),
                    "/v0/item/7.json" => StubHttpMessageHandler.Json(Story(7, score: 1)),
                    "/v0/item/9.json" => StubHttpMessageHandler.Json(Story(9, score: 2)),
                    _ => new(HttpStatusCode.NotFound),
                };
            });
            using TestHost host = new(handler);
            await host.Service.GetStoriesByTypeAsync("best", 5, ScoreThenIdDesc, TestContext.Current.CancellationToken);
            var paths = handler.Requests.Select(u => u.AbsolutePath).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("/v0/beststories.json", paths);
            Assert.Contains("/v0/item/7.json", paths);
            Assert.Contains("/v0/item/9.json", paths);
            Assert.Equal(3, handler.Requests.Count);
        }

        [Fact]
        public async Task GetStoriesByTypeAsync_SecondCall_ServesFromCache()
        {
            using StubHttpMessageHandler handler = new(req =>
            {
                return req.RequestUri!.AbsolutePath switch
                {
                    "/v0/topstories.json" => StubHttpMessageHandler.Json(new[] { 1, 2 }),
                    "/v0/item/1.json" => StubHttpMessageHandler.Json(Story(1, score: 10)),
                    "/v0/item/2.json" => StubHttpMessageHandler.Json(Story(2, score: 20)),
                    _ => new(HttpStatusCode.NotFound),
                };
            });
            using TestHost host = new(handler);
            await host.Service.GetStoriesByTypeAsync("top", 5, ScoreThenIdDesc, TestContext.Current.CancellationToken);
            var firstCallCount = handler.Requests.Count;
            await host.Service.GetStoriesByTypeAsync("top", 5, ScoreThenIdDesc, TestContext.Current.CancellationToken);
            Assert.Equal(firstCallCount, handler.Requests.Count);
        }

        [Fact]
        public async Task GetStoriesByTypeAsync_WhenItemFetchFails_DoesNotCacheFailure()
        {
            var attempts = 0;
            using StubHttpMessageHandler handler = new(req =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if(path == "/v0/topstories.json")
                {
                    return StubHttpMessageHandler.Json(new[] { 42 });
                }
                if(path == "/v0/item/42.json")
                {
                    return Interlocked.Increment(ref attempts) == 1
                        ? new(HttpStatusCode.InternalServerError)
                        : StubHttpMessageHandler.Json(Story(42, score: 99));
                }
                return new(HttpStatusCode.NotFound);
            });
            using TestHost host = new(handler);
            await Assert.ThrowsAsync<HttpRequestException>(
                () => host.Service.GetStoriesByTypeAsync("top", 1, ScoreThenIdDesc, TestContext.Current.CancellationToken));
            var stories = await host.Service.GetStoriesByTypeAsync("top", 1, ScoreThenIdDesc, TestContext.Current.CancellationToken);
            var only = Assert.Single(stories);
            Assert.Equal(42, only.Id);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public async Task GetStoriesByTypeAsync_PropagatesCancellation()
        {
            using StubHttpMessageHandler handler = new(_ => StubHttpMessageHandler.Json(Array.Empty<int>()));
            using TestHost host = new(handler);
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => host.Service.GetStoriesByTypeAsync("top", 1, ScoreThenIdDesc, cts.Token));
        }
    }
}
