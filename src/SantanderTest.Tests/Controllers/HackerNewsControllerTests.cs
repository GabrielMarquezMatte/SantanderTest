using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using SantanderTest.Tests.Infrastructure;
using SantanderTest.Api.Dtos;
using SantanderTest.Api.Responses;

namespace SantanderTest.Tests.Controllers
{
    public sealed class HackerNewsControllerTests
    {
        private static readonly int[] TwentyIds = [.. Enumerable.Range(1, 20)];
        private static HackerNewsDto Story(int id, int score = 0, long time = 0L, string title = "t", string by = "u",
                                           string type = "story", int descendants = 0, Uri? url = null,
                                           string? text = null)
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
                Url = url,
                Text = text,
            };
        }

        private static StubHttpMessageHandler BuildStub(Dictionary<string, IReadOnlyList<int>> idsByType, Dictionary<int, HackerNewsDto> stories)
        {
            return new(req =>
            {
                var path = req.RequestUri!.AbsolutePath;
                const string idsPrefix = "/v0/";
                const string idsSuffix = "stories.json";
                if (path.StartsWith(idsPrefix, StringComparison.Ordinal) && path.EndsWith(idsSuffix, StringComparison.Ordinal))
                {
                    var type = path.AsSpan(idsPrefix.Length..^idsSuffix.Length);
                    var alternateLookup = idsByType.GetAlternateLookup<ReadOnlySpan<char>>();
                    if (alternateLookup.TryGetValue(type, out var ids))
                    {
                        return StubHttpMessageHandler.Json(ids);
                    }
                }
                const string itemPrefix = "/v0/item/";
                const string itemSuffix = ".json";
                if (!path.StartsWith(itemPrefix, StringComparison.Ordinal) || !path.EndsWith(itemSuffix, StringComparison.Ordinal))
                {
                    return new(HttpStatusCode.NotFound);
                }
                var idStr = path[itemPrefix.Length..^itemSuffix.Length];
                if (int.TryParse(idStr, CultureInfo.InvariantCulture, out var id) && stories.TryGetValue(id, out var dto))
                {
                    return StubHttpMessageHandler.Json(dto);

                }
                return new(HttpStatusCode.NotFound);
            });
        }

        [Fact]
        public async Task Get_TopStories_MapsDtoFieldsToResponse()
        {
            var ct = TestContext.Current.CancellationToken;
            const long unixSeconds = 1_704_067_200L; // 2024-01-01T00:00:00Z
            var dto = Story(101, score: 75, time: unixSeconds, title: "Hello", by: "alice", type: "story", descendants: 9, url: new Uri("https://news.example/1"), text: "body");
            await using HackerNewsApiFactory factory = new()
            {
                Handler = BuildStub(
                    new(StringComparer.Ordinal) { ["top"] = [101] },
                    new(){ [101] = dto }),
            };
            using var client = factory.CreateClient();
            var stories = await client.GetFromJsonAsync<HackerNewsResponse[]>("/hackernews/top", ct);
            Assert.NotNull(stories);
            var only = Assert.Single(stories);
            Assert.Equal(101, only.Id);
            Assert.Equal("Hello", only.Title);
            Assert.Equal("alice", only.PostedBy);
            Assert.Equal(75, only.Score);
            Assert.Equal(9, only.CommentCount);
            Assert.Equal("story", only.Type);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).DateTime, only.Time);
            Assert.Equal(new Uri("https://news.example/1"), only.Url);
            Assert.Equal("body", only.Text);
        }

        [Fact]
        public async Task Get_RespectsCountQueryParameter()
        {
            var ct = TestContext.Current.CancellationToken;
            var stories = TwentyIds.ToDictionary(i => i, i => Story(i, score: i));
            await using HackerNewsApiFactory factory = new()
            {
                Handler = BuildStub(
                    new(StringComparer.Ordinal) { ["top"] = TwentyIds },
                    stories),
            };
            using var client = factory.CreateClient();

            var result = await client.GetFromJsonAsync<HackerNewsResponse[]>("/hackernews/top?count=3", ct);

            Assert.NotNull(result);
            Assert.Equal(3, result.Length);
        }

        [Fact]
        public async Task Get_DefaultsCountToTen_WhenNotSpecified()
        {
            var ct = TestContext.Current.CancellationToken;
            var stories = TwentyIds.ToDictionary(i => i, i => Story(i, score: i));
            await using HackerNewsApiFactory factory = new()
            {
                Handler = BuildStub(
                    new(StringComparer.Ordinal) { ["top"] = TwentyIds },
                    stories),
            };
            using var client = factory.CreateClient();

            var result = await client.GetFromJsonAsync<HackerNewsResponse[]>("/hackernews/top", ct);

            Assert.NotNull(result);
            Assert.Equal(10, result.Length);
        }

        [Fact]
        public async Task Get_OrdersByScoreDescending()
        {
            var ct = TestContext.Current.CancellationToken;
            Dictionary<int, HackerNewsDto> stories = new()
            {
                [1] = Story(1, score: 5),
                [2] = Story(2, score: 100),
                [3] = Story(3, score: 50),
            };
            await using HackerNewsApiFactory factory = new()
            {
                Handler = BuildStub(
                    new(StringComparer.Ordinal) { ["top"] = [1, 2, 3] },
                    stories),
            };
            using var client = factory.CreateClient();
            var result = await client.GetFromJsonAsync<HackerNewsResponse[]>("/hackernews/top", ct);
            Assert.NotNull(result);
            Assert.Equal([2, 3, 1], result.Select(s => s.Id));
        }

        [Fact]
        public async Task Get_WhenUpstreamFails_ReturnsServerError()
        {
            var ct = TestContext.Current.CancellationToken;
            await using HackerNewsApiFactory factory = new()
            {
                Handler = new(_ => new(HttpStatusCode.InternalServerError)),
            };
            using var client = factory.CreateClient();
            using var response = await client.GetAsync(new Uri("/hackernews/top", UriKind.Relative), ct);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(ct);
            Assert.Contains("error", body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
