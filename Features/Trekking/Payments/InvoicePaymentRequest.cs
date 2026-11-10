using prohpharmacy_trekking_app.Features.Trekking.Enums;

namespace prohpharmacy_trekking_app.Features.Trekking.Payments;

public sealed class InvoicePaymentRequest
{
    public Guid ClientGeneratedId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public string? Reference { get; set; }
    public DateTime? RecordedAt { get; set; }
}
