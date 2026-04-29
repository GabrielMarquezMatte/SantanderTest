using BitFaster.Caching;
using BitFaster.Caching.Lru;
using Microsoft.Extensions.Options;
using SantanderTest.Api.Configs;
using SantanderTest.Api.Dtos;

namespace SantanderTest.Api.Services
{
    public sealed class HackerNewsService(HttpClient httpClient, IOptions<HackerNewsConfig> configuration, ILogger<HackerNewsService> logger)
    {
        private static IAsyncCache<TKey, T> BuildCache<TKey, T>(IEqualityComparer<TKey>? comparer = null) where TKey : notnull
        {
            return new ConcurrentLruBuilder<TKey, T>().WithAtomicGetOrAdd()
                                                      .WithCapacity(3_000)
                                                      .WithKeyComparer(comparer ?? EqualityComparer<TKey>.Default)
                                                      .WithExpireAfterWrite(TimeSpan.FromMinutes(5))
                                                      .AsAsyncCache()
                                                      .Build();
        }
        private readonly IAsyncCache<string, List<int>> _storiesCache = BuildCache<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly IAsyncCache<int, HackerNewsDto> _responsesCache = BuildCache<int, HackerNewsDto>();
        private async Task<List<int>> GetStoriesImplAsync(string type, CancellationToken cancellationToken)
        {
            var baseUrl = configuration.Value.BaseUrl;
            Uri url = new(baseUrl, $"v0/{type}stories.json");
            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadFromJsonAsync<List<int>>(cancellationToken).ConfigureAwait(false);
            return content ?? [];
        }
        private ValueTask<List<int>> GetStoriesAsync(string type, CancellationToken cancellationToken)
        {
            return _storiesCache.GetOrAddAsync(type, GetStoriesImplAsync, cancellationToken);
        }
        private async Task<HackerNewsDto> GetStoryImplAsync(int id, CancellationToken cancellationToken)
        {
            var baseUrl = configuration.Value.BaseUrl;
            Uri url = new(baseUrl, $"v0/item/{id}.json");
            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadFromJsonAsync<HackerNewsDto>(cancellationToken).ConfigureAwait(false);
            return content ?? throw new InvalidOperationException($"Failed to deserialize response for story with ID {id}.");
        }
        private async Task<HackerNewsDto> GetStoryAsync(int id, CancellationToken cancellationToken)
        {
            var inCache = true;
            var response = await _responsesCache.GetOrAddAsync(id, async (storyId, ct) =>
            {
                var result = await GetStoryImplAsync(storyId, ct).ConfigureAwait(false);
                inCache = false;
                return result;
            }, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Story with ID {StoryId} was {CacheStatus} retrieved from cache.", id, inCache ? "successfully" : "not");
            return response;
        }
        public async Task<HackerNewsDto[]> GetStoriesByTypeAsync(string type, int count, IComparer<HackerNewsDto>? orderBySelector, CancellationToken cancellationToken)
        {
            var storyIds = await GetStoriesAsync(type, cancellationToken).ConfigureAwait(false);
            var tasks = storyIds.Select(id => GetStoryAsync(id, cancellationToken));
            SortedSet<HackerNewsDto> sortedStories = new(orderBySelector);
            await foreach(var task in Task.WhenEach(tasks).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                var result = await task.ConfigureAwait(false);
                sortedStories.Add(result);
            }
            return [.. sortedStories.Take(count)];
        }
    }
}