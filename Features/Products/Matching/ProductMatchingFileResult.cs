using prohpharmacy_trekking_app.Services.DocumentExtraction;

namespace prohpharmacy_trekking_app.Features.Products.Matching;

public sealed class ProductMatchingFileResult
{
    public int FileIndex { get; set; }
    public string FileName { get; set; } = "";
    public string Status { get; set; } = "Failed";
    public List<DocumentIssue> Errors { get; set; } = [];
    public List<MatchedProductRow> Rows { get; set; } = [];
}
