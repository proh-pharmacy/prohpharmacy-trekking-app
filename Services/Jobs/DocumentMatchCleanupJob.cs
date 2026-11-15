using Hangfire;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Products.Matching.Entities;
using prohpharmacy_trekking_app.Services.DocumentExtraction;

namespace prohpharmacy_trekking_app.Services.Jobs;

public sealed class DocumentMatchCleanupJob(AppDbContext db, DocumentMatchFileStorage storage)
{
    [DisableConcurrentExecution(300)]
    public async Task RemoveExpiredAsync(CancellationToken cancellationToken)
    {
        var expired = await db.DocumentMatchJobs
            .Where(job => job.ExpiresAt <= DateTime.UtcNow &&
                (job.Status == DocumentMatchJobStatus.Completed ||
                 job.Status == DocumentMatchJobStatus.PartiallyCompleted ||
                 job.Status == DocumentMatchJobStatus.Failed))
            .ToListAsync(cancellationToken);
        foreach (var job in expired)
        {
            storage.Delete(job.Id);
            db.DocumentMatchJobs.Remove(job);
        }
        if (expired.Count > 0) await db.SaveChangesAsync(cancellationToken);
    }
}
