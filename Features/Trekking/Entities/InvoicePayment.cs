using prohpharmacy_trekking_app.Features.Trekking.Enums;

namespace prohpharmacy_trekking_app.Features.Trekking.Entities;

public class InvoicePayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleInvoiceId { get; set; }
    public Guid ClientGeneratedId { get; set; }
    public string? ReceiptNumber { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public string? Reference { get; set; }
    public Guid RecordedByStaffId { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public decimal InvoiceTotalAtPayment { get; set; }
    public decimal BalanceAfterPayment { get; set; }
    public bool IsLegacy { get; set; }
    public SaleInvoice Invoice { get; set; } = null!;
}
