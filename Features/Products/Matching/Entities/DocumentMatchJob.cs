namespace prohpharmacy_trekking_app.Features.Products.Matching.Entities;

public sealed class DocumentMatchJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DocumentMatchJobStatus Status { get; set; } = DocumentMatchJobStatus.Queued;
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int? CurrentFileIndex { get; set; }
    public int? CurrentPage { get; set; }
    public int? CurrentFilePages { get; set; }
    public int AttemptCount { get; set; }
    public string InputManifestJson { get; set; } = "[]";
    public string ExcelMappingsJson { get; set; } = "[]";
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
}
