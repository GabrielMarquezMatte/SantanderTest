using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.OpenApi;
using SantanderTest.Api.Configs;
using SantanderTest.Api.Handlers;
using SantanderTest.Api.Services;

namespace SantanderTest.Api
{
    public sealed class Program
    {
        private Program() { }
        private static void ConfigureSwagger(IServiceCollection services)
        {
            services.AddSwaggerGen(config =>
            {
                config.SwaggerDoc("v1", new OpenApiInfo { Title = "SantanderTest API", Version = "v1" });
                config.EnableAnnotations();
            });
        }
        private static void ConfigureServices(WebApplicationBuilder builder)
        {
            var services = builder.Services;
            services.AddHttpClient();
            services.AddOptions<HackerNewsConfig>()
                            .Bind(builder.Configuration.GetSection("HackerNews"))
                            .ValidateDataAnnotations()
                            .ValidateOnStart();
            services.AddSingleton<HackerNewsService>();
            services.AddExceptionHandler<ExceptionHandler>();
            services.AddProblemDetails();
            services.AddResponseCompression(options =>
            {
                options.Providers.Add<GzipCompressionProvider>();
                options.EnableForHttps = true;
            });
            services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
            services.AddMvc(x => x.SuppressAsyncSuffixInActionNames = false);
            services.AddControllers()
                .AddJsonOptions(x =>
                {
                    x.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                    x.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                    x.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                });
            ConfigureSwagger(services);
        }
        private static void ConfigureMiddleware(WebApplication app)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "SantanderTest API v1");
                c.EnablePersistAuthorization();
                c.DefaultModelsExpandDepth(-1);
                c.EnableTryItOutByDefault();
                c.DisplayRequestDuration();
            });
            app.UseCors(builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
            app.UseRouting();
            app.UseResponseCompression();
            app.UseExceptionHandler();
            app.MapControllers();
        }
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            ConfigureServices(builder);
            var app = builder.Build();
            await using(app.ConfigureAwait(false))
            {
                ConfigureMiddleware(app);
                await app.RunAsync().ConfigureAwait(false);
            }
        }
    }
}