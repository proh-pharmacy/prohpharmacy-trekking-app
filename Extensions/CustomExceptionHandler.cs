using Microsoft.AspNetCore.Diagnostics;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Extensions
{
    public sealed class CustomExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<CustomExceptionHandler> _logger;

        public CustomExceptionHandler(ILogger<CustomExceptionHandler> logger)
        {
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);

            var (statusCode, title) = exception switch
            {
                UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
                ArgumentException or ArgumentNullException => (StatusCodes.Status400BadRequest, "Bad Request"),
                KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
                InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid Operation"),
                _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
            };

            httpContext.Response.StatusCode = statusCode;

            var problemDetails = new
            {
                code = statusCode.ToString(),
                title,
                message = exception.Message,
                path = httpContext.Request.Path
            };

            await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
            return true;
        }
    }
}
