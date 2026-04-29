using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SantanderTest.Api;

namespace SantanderTest.Tests.Infrastructure
{
    internal sealed class HackerNewsApiFactory : WebApplicationFactory<Program>
    {
        public Uri BaseUrl { get; } = new("https://hacker-news.test/");
        public StubHttpMessageHandler Handler { get; init; } = new(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["HackerNews:BaseUrl"] = BaseUrl.ToString(),
                });
            });
            builder.ConfigureServices(services => services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => Handler)));
        }
    }
}
