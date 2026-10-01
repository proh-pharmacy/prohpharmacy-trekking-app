using prohpharmacy_trekking_app.Features.Trekking.Enums;

namespace prohpharmacy_trekking_app.Features.Trekking.Entities;

public class SaleInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? InvoiceNumber { get; set; }
    public Guid TrekkingTripStopId { get; set; }
    public Guid TrekkingTripId { get; set; }
    public Guid CustomerAccountId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Balance { get; set; }
    public SaleInvoiceStatus Status { get; set; } = SaleInvoiceStatus.Issued;
    public Guid? ClientGeneratedId { get; set; }
    public bool CreatedOffline { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public TrekkingTripStop Stop { get; set; } = null!;
}
