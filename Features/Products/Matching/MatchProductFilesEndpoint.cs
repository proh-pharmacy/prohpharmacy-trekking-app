using System.Text.Json;
using Carter;
using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Products.Matching.Entities;
using prohpharmacy_trekking_app.Services.DocumentExtraction;
using prohpharmacy_trekking_app.Services.Jobs;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Products.Matching;

public sealed class MatchProductFilesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/products/match-files", async (HttpRequest request,
            IValidator<MatchProductFilesCommand> validator,
            AppDbContext db,
            IBackgroundJobClient backgroundJobs,
            DocumentMatchFileStorage storage,
            IOptions<DocumentMatchingOptions> options,
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
            var command = new MatchProductFilesCommand
            {
                Files = form.Files.GetFiles("files").ToList(), ExcelMappings = mappings
            };
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
                return Results.UnprocessableEntity(Error.ValidationError(validation));

            var job = new DocumentMatchJob
            {
                TotalFiles = command.Files.Count,
                ExcelMappingsJson = JsonSerializer.Serialize(mappings, JsonOptions),
                ExpiresAt = DateTime.UtcNow.AddHours(options.Value.JobRetentionHours)
            };
            try
            {
                var manifest = await storage.SaveAsync(job.Id, command.Files, cancellationToken);
                job.InputManifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
                db.DocumentMatchJobs.Add(job);
                await db.SaveChangesAsync(cancellationToken);
                backgroundJobs.Enqueue<DocumentMatchProcessingJob>(
                    queued => queued.ProcessAsync(job.Id, CancellationToken.None));
            }
            catch (Exception)
            {
                try { storage.Delete(job.Id); } catch { }
                if (db.Entry(job).State is EntityState.Unchanged or EntityState.Modified)
                {
                    job.Status = DocumentMatchJobStatus.Failed;
                    job.ErrorMessage = "The document could not be queued. Please try again.";
                    job.CompletedAt = DateTime.UtcNow;
                    try { await db.SaveChangesAsync(CancellationToken.None); } catch { }
                }
                return Results.Json(Error.ServerError("The document could not be queued. Please try again."),
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var pollUrl = $"/api/v1/products/match-files/jobs/{job.Id}";
            request.HttpContext.Response.Headers["Retry-After"] = "2";
            return Results.Accepted(pollUrl, new DocumentMatchJobAcceptedResponse
            {
                JobId = job.Id,
                Status = job.Status,
                PollUrl = pollUrl,
                ResultUrl = $"{pollUrl}/result"
            });
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
        .WithSummary("Queue product extraction and matching from Excel, PDF or photos")
        .WithDescription("Multipart fields: files (multiple), excelMappings (optional JSON array keyed by zero-based fileIndex). Returns a job that the frontend polls. No vehicle or trek ID.")
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
        .Produces<DocumentMatchJobAcceptedResponse>(202)
        .Produces<Error>(422)
        .Produces<Error>(413)
        .Produces(401);

        app.MapGet("api/v1/products/match-files/jobs/{jobId:guid}", async (
            Guid jobId, HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            var job = await db.DocumentMatchJobs.AsNoTracking()
                .SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            return job is null ? Results.NotFound() : Results.Ok(ToStatusResponse(job));
        })
        .WithTags("Products")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Get document matching job progress")
        .Produces<DocumentMatchJobStatusResponse>(200)
        .Produces(404);

        app.MapGet("api/v1/products/match-files/jobs/{jobId:guid}/result", async (
            Guid jobId, HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            var job = await db.DocumentMatchJobs.AsNoTracking()
                .SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            if (job is null) return Results.NotFound();
            if (job.ResultJson is null)
                return Results.Json(ToStatusResponse(job), statusCode: StatusCodes.Status409Conflict);
            return Results.Content(job.ResultJson, "application/json", statusCode: StatusCodes.Status200OK);
        })
        .WithTags("Products")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Get a completed document matching result")
        .Produces<MatchProductFilesResponse>(200)
        .Produces<DocumentMatchJobStatusResponse>(409)
        .Produces(404);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static DocumentMatchJobStatusResponse ToStatusResponse(DocumentMatchJob job)
    {
        var terminal = job.Status is DocumentMatchJobStatus.Completed or
            DocumentMatchJobStatus.PartiallyCompleted or DocumentMatchJobStatus.Failed;
        var fileProgress = job.TotalFiles == 0 ? 0d : (double)job.ProcessedFiles / job.TotalFiles;
        if (!terminal && job.CurrentFilePages is > 0 && job.CurrentPage.HasValue && job.TotalFiles > 0)
            fileProgress = (job.ProcessedFiles +
                Math.Clamp((double)job.CurrentPage.Value / job.CurrentFilePages.Value, 0, 1)) / job.TotalFiles;
        var percentage = terminal ? 100 : Math.Min(99, (int)Math.Floor(fileProgress * 100));
        var message = job.Status switch
        {
            DocumentMatchJobStatus.Queued => "Waiting to process the uploaded documents.",
            DocumentMatchJobStatus.Processing when job.CurrentFilePages is > 0 && job.CurrentPage is > 0 =>
                $"Processing file {(job.CurrentFileIndex ?? 0) + 1} of {job.TotalFiles}, page {job.CurrentPage ?? 0} of {job.CurrentFilePages}.",
            DocumentMatchJobStatus.Processing =>
                $"Processing file {(job.CurrentFileIndex ?? 0) + 1} of {job.TotalFiles}.",
            DocumentMatchJobStatus.Completed => "Document matching completed.",
            DocumentMatchJobStatus.PartiallyCompleted => "Document matching completed with some file or page errors.",
            _ => job.ErrorMessage ?? "Document matching failed."
        };
        return new DocumentMatchJobStatusResponse
        {
            JobId = job.Id,
            Status = job.Status,
            TotalFiles = job.TotalFiles,
            ProcessedFiles = job.ProcessedFiles,
            CurrentFileIndex = job.CurrentFileIndex,
            CurrentPage = job.CurrentPage,
            CurrentFilePages = job.CurrentFilePages,
            ProgressPercentage = percentage,
            Message = message,
            ErrorMessage = job.ErrorMessage,
            ResultUrl = job.ResultJson is null ? null : $"/api/v1/products/match-files/jobs/{job.Id}/result",
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt
        };
    }
}
