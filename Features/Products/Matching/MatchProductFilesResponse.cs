namespace prohpharmacy_trekking_app.Features.Products.Matching;

public sealed class MatchProductFilesResponse
{
    public ProductMatchingSummary Summary { get; set; } = new();
    public List<ProductMatchingFileResult> Files { get; set; } = [];
    public List<AggregatedProduct> AggregatedProducts { get; set; } = [];
}
