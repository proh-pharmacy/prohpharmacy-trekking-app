using prohpharmacy_trekking_app.Features.Products.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;

namespace prohpharmacy_trekking_app.Features.Trekking.Entities;

public class TrekkingTripStopProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrekkingTripStopId { get; set; }
    public Guid ProductId { get; set; }
    public decimal PlannedBasicQuantity { get; set; }
    public decimal? PlannedPackagingQuantity { get; set; }
    public decimal BasicUnitPrice { get; set; }
    public decimal? PackagingUnitPrice { get; set; }
    public decimal? BasicQtyDelivered { get; set; }
    public decimal? PackagingQtyDelivered { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public decimal? AmountDue { get; set; }
    public decimal? AmtPaid { get; set; }
    public decimal? Balance { get; set; }
    public bool IsUnplanned { get; set; }
    public string? Notes { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public Guid? ClientGeneratedId { get; set; }

    public TrekkingTripStop TrekkingTripStop { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
