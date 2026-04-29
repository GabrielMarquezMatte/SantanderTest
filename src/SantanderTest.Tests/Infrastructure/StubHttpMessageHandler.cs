using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

namespace EmagineTest.Tests.Infrastructure
{
    internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly ConcurrentBag<Uri> _requests = [];
        public IReadOnlyCollection<Uri> Requests => [.. _requests];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(request.RequestUri is { } uri)
            {
                _requests.Add(uri);
            }
            HttpResponseMessage response;
            try
            {
                response = responder(request);
            }
            catch(HttpRequestException) { throw; }
            return Task.FromResult(response);
        }

        public static HttpResponseMessage Json<T>(T value, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            return new(statusCode)
            {
                Content = JsonContent.Create(value),
            };
        }
    }
}
