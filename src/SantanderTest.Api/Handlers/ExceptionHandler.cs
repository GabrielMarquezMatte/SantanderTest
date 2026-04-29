using Microsoft.AspNetCore.Diagnostics;

namespace SantanderTest.Api.Handlers
{
    public sealed partial class ExceptionHandler(ILogger<ExceptionHandler> logger) : IExceptionHandler
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "An unhandled exception occurred while processing the request.")]
        private partial void LogAnUnhandledExceptionOccurred(Exception exception);
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            LogAnUnhandledExceptionOccurred(exception);
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await httpContext.Response.WriteAsync("{\"error\": \"An unexpected error occurred while processing the request.\"}", cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}