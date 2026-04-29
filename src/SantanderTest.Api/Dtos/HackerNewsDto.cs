using System.ComponentModel.DataAnnotations;

namespace SantanderTest.Api.Dtos
{
    public sealed class HackerNewsDto
    {
        [Required]
        public int Id { get; set; }
        [Required]
        public required string Title { get; set; }
        public Uri? Url { get; set; }
        public string? Text { get; set; }
        [Required]
        public required string By { get; set; }
        [Required]
        public long Time { get; set; }
        [Required]
        public int Score { get; set; }
        [Required]
        public required string Type { get; set; }
        [Required]
        public int Descendants { get; set; }
        public IEnumerable<int> Kids { get; set; } = [];
    }
}