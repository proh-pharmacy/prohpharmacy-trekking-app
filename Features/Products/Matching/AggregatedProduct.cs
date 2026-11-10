namespace prohpharmacy_trekking_app.Features.Products.Matching;

public sealed class AggregatedProduct
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal? BasicQty { get; set; }
    public decimal? PackagingQty { get; set; }
    public bool RequiresReview { get; set; } = true;
    public List<string> SourceRows { get; set; } = [];
}
