namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class ExcelColumnMapping
{
    public int FileIndex { get; set; }
    public string? Worksheet { get; set; }
    public int HeaderRow { get; set; } = 1;
    public string? ProductNameColumn { get; set; }
    public string? QuantityColumn { get; set; }
    public string? UnitColumn { get; set; }
    public string? BasicQtyColumn { get; set; }
    public string? PackagingQtyColumn { get; set; }
}
