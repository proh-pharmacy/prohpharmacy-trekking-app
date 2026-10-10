using System.Text.Json;
using Carter;
using MediatR;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.DocumentExtraction;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Products.Matching;

public sealed class MatchProductFilesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/products/match-files", async (HttpRequest request, ISender sender,
            CancellationToken cancellationToken) =>
        {
            if (request.ContentType is null ||
                !request.ContentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                return Results.UnprocessableEntity(Error.BadRequest("Request must be multipart/form-data."));
            var bodyLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false })
                bodyLimit.MaxRequestBodySize = DocumentMatchingOptions.RequestBodyLimit;
            if (request.ContentLength > DocumentMatchingOptions.RequestBodyLimit)
                return Results.Json(Error.BadRequest("The upload exceeds the request-size limit."), statusCode: 413);

            IFormCollection form;
            request.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
            {
                MultipartBodyLengthLimit = DocumentMatchingOptions.RequestBodyLimit,
                BufferBodyLengthLimit = DocumentMatchingOptions.RequestBodyLimit,
                ValueLengthLimit = 16384,
                ValueCountLimit = 10
            }));
            try { form = await request.ReadFormAsync(cancellationToken); }
            catch (InvalidDataException)
            {
                return Results.UnprocessableEntity(Error.BadRequest("Invalid multipart upload or form limits exceeded."));
            }
            if (form.Files.Any(f => f.Name != "files"))
                return Results.UnprocessableEntity(Error.BadRequest("Upload all files under the 'files' field."));
            List<ExcelColumnMapping> mappings = [];
            if (form.TryGetValue("excelMappings", out var json) && !string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    mappings = JsonSerializer.Deserialize<List<ExcelColumnMapping>>(json.ToString(),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
                    if (mappings.Any(m => m is null)) throw new JsonException();
                }
                catch (JsonException)
                {
                    return Results.UnprocessableEntity(Error.BadRequest("'excelMappings' must be a JSON array of column mappings."));
                }
            }
            var result = await sender.Send(new MatchProductFilesCommand
            {
                Files = form.Files.GetFiles("files").ToList(), ExcelMappings = mappings
            }, cancellationToken);
            return result.IsFailure ? Results.UnprocessableEntity(result.Error) : Results.Ok(result.Value);
        })
        .WithMetadata(new RequestSizeLimitAttribute(DocumentMatchingOptions.RequestBodyLimit),
            new RequestFormLimitsAttribute
            {
                MultipartBodyLengthLimit = DocumentMatchingOptions.RequestBodyLimit,
                ValueLengthLimit = 16384, ValueCountLimit = 10
            })
        .DisableAntiforgery()
        .WithTags("Products")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Extract and match products from Excel, PDF or photos without changing stock")
        .WithDescription("Multipart fields: files (multiple), excelMappings (optional JSON array keyed by zero-based fileIndex). Returns per-file results for frontend review. No vehicle or trek ID.")
        .WithOpenApi(operation =>
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new()
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object", Required = new HashSet<string> { "files" },
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["files"] = new() { Type = "array", Items = new() { Type = "string", Format = "binary" } },
                                ["excelMappings"] = new() { Type = "string", Description = "JSON array of Excel column mappings keyed by fileIndex." }
                            }
                        }
                    }
                }
            };
            return operation;
        })
        .Produces<MatchProductFilesResponse>(200)
        .Produces<Error>(422)
        .Produces<Error>(413)
        .Produces(401);
    }
}
