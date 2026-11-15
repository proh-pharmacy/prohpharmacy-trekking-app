using System.Text.Json;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Products.Matching;
using prohpharmacy_trekking_app.Features.Products.Matching.Entities;
using prohpharmacy_trekking_app.Services.DocumentExtraction;

namespace prohpharmacy_trekking_app.Services.Jobs;

public sealed class DocumentMatchProcessingJob(
    AppDbContext db,
    ISender sender,
    DocumentMatchFileStorage storage,
    ILogger<DocumentMatchProcessingJob> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Queue("document-matching")]
    [AutomaticRetry(Attempts = 1, DelaysInSeconds = new[] { 10 }, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await db.DocumentMatchJobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null || job.Status is DocumentMatchJobStatus.Completed or
            DocumentMatchJobStatus.PartiallyCompleted ||
            job.Status == DocumentMatchJobStatus.Failed && job.ResultJson is not null)
            return;

        job.AttemptCount++;
        job.Status = DocumentMatchJobStatus.Processing;
        job.StartedAt ??= DateTime.UtcNow;
        job.ErrorMessage = null;
        await db.SaveChangesAsync(cancellationToken);

        var streams = new List<FileStream>();
        var deleteStoredFiles = false;
        try
        {
            var manifest = JsonSerializer.Deserialize<List<StoredDocumentMatchFile>>(
                job.InputManifestJson, JsonOptions) ?? [];
            var mappings = JsonSerializer.Deserialize<List<ExcelColumnMapping>>(
                job.ExcelMappingsJson, JsonOptions) ?? [];
            var files = new List<IFormFile>(manifest.Count);
            foreach (var stored in manifest.OrderBy(f => f.FileIndex))
            {
                var stream = storage.OpenRead(jobId, stored);
                streams.Add(stream);
                files.Add(new FormFile(stream, 0, stored.Length, "files", stored.OriginalName)
                {
                    Headers = new HeaderDictionary(),
                    ContentType = stored.ContentType
                });
            }

            var result = await sender.Send(new MatchProductFilesCommand
            {
                Files = files,
                ExcelMappings = mappings,
                JobId = jobId
            }, cancellationToken);

            if (result.IsFailure)
                throw new InvalidOperationException(result.Error.Message);

            job.ResultJson = JsonSerializer.Serialize(result.Value, JsonOptions);
            var failedFiles = result.Value.Summary.FailedFiles;
            var partialFiles = result.Value.Summary.PartialFiles;
            job.Status = failedFiles == result.Value.Summary.TotalFiles
                ? DocumentMatchJobStatus.Failed
                : failedFiles > 0 || partialFiles > 0
                    ? DocumentMatchJobStatus.PartiallyCompleted
                    : DocumentMatchJobStatus.Completed;
            job.ProcessedFiles = job.TotalFiles;
            job.CurrentFileIndex = null;
            job.CurrentPage = null;
            job.CurrentFilePages = null;
            job.ErrorMessage = job.Status == DocumentMatchJobStatus.Failed
                ? "No document files could be processed."
                : null;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            deleteStoredFiles = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = DocumentMatchJobStatus.Queued;
            job.ErrorMessage = "Document processing was interrupted and will resume shortly.";
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Document matching job {JobId} failed on attempt {AttemptCount}.",
                jobId, job.AttemptCount);
            if (job.AttemptCount < 2)
            {
                job.Status = DocumentMatchJobStatus.Queued;
                job.ErrorMessage = "Document processing was interrupted and will be retried.";
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            job.Status = DocumentMatchJobStatus.Failed;
            job.ErrorMessage = "The document could not be processed. Please try again.";
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            deleteStoredFiles = true;
        }
        finally
        {
            foreach (var stream in streams) await stream.DisposeAsync();
            if (deleteStoredFiles)
            {
                try { storage.Delete(jobId); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Stored files for document matching job {JobId} could not be removed.", jobId);
                }
            }
        }
    }
}
