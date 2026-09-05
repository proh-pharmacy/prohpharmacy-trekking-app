namespace prohpharmacy_trekking_app.Middlewares;

using System.Net;
using System.Text.Json;

public class JsonExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JsonExceptionHandlingMiddleware> _logger;

    public JsonExceptionHandlingMiddleware(RequestDelegate next, ILogger<JsonExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException ex) when (ex.InnerException is JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Malformed JSON in request body");

            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "application/json";

            var errorResponse = new
            {
                code = "400",
                message = jsonEx.Message
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var errorResponse = new
            {
                code = "500",
                message = ex.Message
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
        }
    }
}
