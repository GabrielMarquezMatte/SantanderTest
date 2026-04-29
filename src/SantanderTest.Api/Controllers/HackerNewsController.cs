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
        private static HackerNewsResponse MapToResponse(HackerNewsDto dto)
        {
            return new(dto.Id, dto.Title, dto.Url, dto.Text, dto.By, DateTimeOffset.FromUnixTimeSeconds(dto.Time),
                       dto.Score, dto.Type, dto.Descendants);
        }
        [HttpGet("{type}")]
        [ProducesResponseType(typeof(IEnumerable<HackerNewsResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetStoriesByTypeAsync([FromRoute, Required, AllowedValues("top", "new", "best", "ask", "show", "job")] string type,
                                                               [FromQuery, Range(1, 100)] int count = 10,
                                                               CancellationToken cancellationToken = default)
        {
            var stories = await hackerNewsService.GetStoriesByTypeAsync(type, count, cancellationToken).ConfigureAwait(false);
            var response = stories.Select(MapToResponse);
            return Ok(response);
        }
    }
}