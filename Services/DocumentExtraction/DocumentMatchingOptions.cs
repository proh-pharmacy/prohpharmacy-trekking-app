namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class DocumentMatchingOptions
{
    public const long RequestBodyLimit = 21 * 1024 * 1024;
    public int MaxFiles { get; set; } = 5;
    public int MaxFileBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxTotalBytes { get; set; } = 20 * 1024 * 1024;
    public int MaxPages { get; set; } = 20;
    public int MaxRowsPerFile { get; set; } = 1000;
    public int MaxTextLength { get; set; } = 1000;
    public int ProcessingTimeoutSeconds { get; set; } = 180;
    public int MaxConcurrentRequests { get; set; } = 2;
    public int MinimumSimilarity { get; set; } = 75;
    public int AmbiguityMargin { get; set; } = 5;
    public int MaxCandidates { get; set; } = 5;
    public string OcrBaseUrl { get; set; } = "";
    public int OcrTimeoutSeconds { get; set; } = 60;
}
