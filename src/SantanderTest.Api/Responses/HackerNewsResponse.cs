using System.ComponentModel.DataAnnotations;

namespace SantanderTest.Api.Responses
{
    public sealed record class HackerNewsResponse([property: Required] int Id, [property: Required] string Title,
                                                  Uri? Url, string? Text, [property: Required] string PostedBy,
                                                  [property: Required] DateTime Time, [property: Required] int Score,
                                                  [property: Required] string Type,
                                                  [property: Required] int CommentCount);
}