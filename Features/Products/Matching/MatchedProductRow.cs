using prohpharmacy_trekking_app.Services.ProductMatching;
using prohpharmacy_trekking_app.Services.DocumentExtraction;

namespace prohpharmacy_trekking_app.Features.Products.Matching;

public sealed class MatchedProductRow
{
    public int? Page { get; set; }
    public string? Worksheet { get; set; }
    public int Row { get; set; }
    public string ExtractedName { get; set; } = "";
    public string? QuantityText { get; set; }
    public string? BasicQtyText { get; set; }
    public string? PackagingQtyText { get; set; }
    public string? UnitText { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? BasicQty { get; set; }
    public decimal? PackagingQty { get; set; }
    public IReadOnlyList<QuantityObservation> QuantityObservations { get; set; } = [];
    public string? BasicUnitText { get; set; }
    public string? PackagingUnitText { get; set; }
    public double? OcrConfidence { get; set; }
    public string MatchStatus { get; set; } = "Unmatched";
    public Guid? SuggestedProductId { get; set; }
    public IReadOnlyList<ProductMatchCandidate> Candidates { get; set; } = [];
    public bool RequiresReview { get; set; } = true;
    public List<string> Warnings { get; set; } = [];
}
