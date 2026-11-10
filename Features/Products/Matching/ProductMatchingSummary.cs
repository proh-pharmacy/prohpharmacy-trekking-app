namespace prohpharmacy_trekking_app.Features.Products.Matching;

public sealed class ProductMatchingSummary
{
    public int TotalFiles { get; set; }
    public int FailedFiles { get; set; }
    public int PartialFiles { get; set; }
    public int TotalRows { get; set; }
    public int Exact { get; set; }
    public int Suggested { get; set; }
    public int Ambiguous { get; set; }
    public int Unmatched { get; set; }
}
