using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SantanderTest.Api.Dtos;
using SantanderTest.Api.Responses;
using SantanderTest.Api.Services;

namespace SantanderTest.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public sealed class HackerNewsController(HackerNewsService hackerNewsService) : ControllerBase
    {
        private sealed class StoryComparer : IComparer<HackerNewsDto>
        {
            public static StoryComparer Instance { get; } = new();
            public int Compare(HackerNewsDto? x, HackerNewsDto? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;
                int scoreComparison = y.Score.CompareTo(x.Score);
                if (scoreComparison != 0) return scoreComparison;
                return y.Time.CompareTo(x.Time);
            }
        }
        private static HackerNewsResponse MapToResponse(HackerNewsDto dto)
        {
            return new(dto.Id, dto.Title, dto.Url, dto.Text, dto.By,
                       DateTimeOffset.FromUnixTimeSeconds(dto.Time).DateTime, dto.Score, dto.Type, dto.Descendants);
        }
        [HttpGet("{type}")]
        public async Task<IActionResult> GetStoriesByTypeAsync([FromRoute, Required, AllowedValues("top", "new", "best", "ask", "show", "job")] string type, [FromQuery] int count = 10, CancellationToken cancellationToken = default)
        {
            var stories = await hackerNewsService.GetStoriesByTypeAsync(type, count, StoryComparer.Instance, cancellationToken).ConfigureAwait(false);
            var response = stories.Select(MapToResponse);
            return Ok(response);
        }
    }
}