using prohpharmacy_trekking_app.Features.Products.Entities;

namespace prohpharmacy_trekking_app.Features.Trekking.Entities;

public class TrekkingTripStockSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrekkingTripId { get; set; }
    public Guid ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string? BasicUnitName { get; set; }
    public string? PackagingUnitName { get; set; }
    public decimal BasicUnitPrice { get; set; }
    public decimal? PackagingUnitPrice { get; set; }

    public decimal BasicQtyAtStart { get; set; }
    public decimal PackagingQtyAtStart { get; set; }
    public decimal BasicQtySold { get; set; }
    public decimal PackagingQtySold { get; set; }
    public decimal BasicQtyRemaining { get; set; }
    public decimal PackagingQtyRemaining { get; set; }

    public decimal RevenueAmount { get; set; }

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    public TrekkingTrip TrekkingTrip { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
