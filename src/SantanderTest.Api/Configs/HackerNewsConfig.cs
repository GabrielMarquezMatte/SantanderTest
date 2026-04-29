using System.ComponentModel.DataAnnotations;

namespace SantanderTest.Api.Configs
{
    public sealed class HackerNewsConfig
    {
        [Required]
        public required Uri BaseUrl { get; init; }
    }
}