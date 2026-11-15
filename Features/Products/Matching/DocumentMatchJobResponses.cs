using prohpharmacy_trekking_app.Features.Products.Matching.Entities;

namespace prohpharmacy_trekking_app.Features.Products.Matching;

public sealed class DocumentMatchJobAcceptedResponse
{
    public Guid JobId { get; set; }
    public DocumentMatchJobStatus Status { get; set; }
    public string PollUrl { get; set; } = "";
    public string ResultUrl { get; set; } = "";
    public int RetryAfterSeconds { get; set; } = 2;
}

public sealed class DocumentMatchJobStatusResponse
{
    public Guid JobId { get; set; }
    public DocumentMatchJobStatus Status { get; set; }
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int? CurrentFileIndex { get; set; }
    public int? CurrentPage { get; set; }
    public int? CurrentFilePages { get; set; }
    public int ProgressPercentage { get; set; }
    public string Message { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? ResultUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
