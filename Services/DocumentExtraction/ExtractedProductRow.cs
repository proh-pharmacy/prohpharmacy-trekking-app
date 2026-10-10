namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class ExtractedProductRow
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
      public List<QuantityObservation> QuantityObservations { get; set; } = [];
    public double? OcrConfidence { get; set; }
    public List<string> Warnings { get; set; } = [];
}
