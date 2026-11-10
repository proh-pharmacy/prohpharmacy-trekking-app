namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class DocumentExtractionResult
{
    public List<ExtractedProductRow> Rows { get; set; } = [];
    public List<DocumentIssue> Errors { get; set; } = [];
}
