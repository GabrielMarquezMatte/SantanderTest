using BitFaster.Caching;
using BitFaster.Caching.Lru;
using Microsoft.Extensions.Options;
using SantanderTest.Api.Configs;
using SantanderTest.Api.Dtos;
using SantanderTest.Api.Objects;

namespace SantanderTest.Api.Services
{
    public sealed class HackerNewsService(HttpClient httpClient, IOptions<HackerNewsConfig> configuration)
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
        private ValueTask<HackerNewsDto> GetStoryAsync(int id, CancellationToken cancellationToken)
        {
            return _responsesCache.GetOrAddAsync(id, GetStoryImplAsync, cancellationToken);
        }
        public async Task<HackerNewsDto[]> GetStoriesByTypeAsync(string type, int count, CancellationToken cancellationToken)
        {
            PriorityQueue<HackerNewsDto, NewsPriority> topStories = new();
            if (count <= 0) return [];
            var storyIds = await GetStoriesAsync(type, cancellationToken).ConfigureAwait(false);
            var tasks = storyIds.Select(id => GetStoryAsync(id, cancellationToken).AsTask());
            await foreach (var task in Task.WhenEach(tasks).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                var story = await task.ConfigureAwait(false);
                NewsPriority priority = new(story.Score, story.Time, story.Id);
                if (topStories.Count < count)
                {
                    topStories.Enqueue(story, priority);
                    continue;
                }
                topStories.EnqueueDequeue(story, priority);
            }
            var result = new HackerNewsDto[topStories.Count];
            var index = result.Length - 1;
            while (topStories.TryDequeue(out var story, out _))
            {
                result[index--] = story;
            }
            return result;
        }
    }
}