using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;

namespace prohpharmacy_trekking_app.Features.Products.Matching;

public sealed class DocumentMatchJobProgressReporter(AppDbContext db)
{
    public async Task FileStartedAsync(Guid jobId, int fileIndex, CancellationToken cancellationToken)
    {
        var job = await db.DocumentMatchJobs.SingleAsync(j => j.Id == jobId, cancellationToken);
        job.CurrentFileIndex = fileIndex;
        job.CurrentPage = 0;
        job.CurrentFilePages = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PageCompletedAsync(Guid jobId, int fileIndex, int completedPages, int totalPages,
        CancellationToken cancellationToken)
    {
        var job = await db.DocumentMatchJobs.SingleAsync(j => j.Id == jobId, cancellationToken);
        job.CurrentFileIndex = fileIndex;
        job.CurrentPage = completedPages;
        job.CurrentFilePages = totalPages;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task FileCompletedAsync(Guid jobId, int fileIndex, CancellationToken cancellationToken)
    {
        var job = await db.DocumentMatchJobs.SingleAsync(j => j.Id == jobId, cancellationToken);
        job.ProcessedFiles = Math.Max(job.ProcessedFiles, fileIndex + 1);
        job.CurrentFileIndex = fileIndex;
        await db.SaveChangesAsync(cancellationToken);
    }
}
