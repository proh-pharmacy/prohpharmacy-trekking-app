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

            var (statusCode, title, message) = exception switch
            {
                BadHttpRequestException { InnerException: System.Text.Json.JsonException jsonEx }
                    => (StatusCodes.Status400BadRequest, "Invalid Request Body", jsonEx.Message),
                BadHttpRequestException ex
                    => (StatusCodes.Status400BadRequest, "Bad Request", ex.Message),
                UnauthorizedAccessException ex
                    => (StatusCodes.Status401Unauthorized, "Unauthorized", ex.Message),
                ArgumentException or ArgumentNullException
                    => (StatusCodes.Status400BadRequest, "Bad Request", exception.Message),
                KeyNotFoundException ex
                    => (StatusCodes.Status404NotFound, "Not Found", ex.Message),
                InvalidOperationException ex
                    => (StatusCodes.Status400BadRequest, "Invalid Operation", ex.Message),
                _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred.")
            };

            httpContext.Response.StatusCode = statusCode;

            var problemDetails = new
            {
                code = statusCode.ToString(),
                title,
                message,
                path = httpContext.Request.Path
            };

            await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
            return true;
        }
    }
}
