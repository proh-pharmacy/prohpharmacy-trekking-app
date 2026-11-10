using prohpharmacy_trekking_app.Features.Trekking.Entities;

namespace prohpharmacy_trekking_app.Features.Trekking.Payments;

public sealed class PaymentReceiptResponse
{
    public Guid PaymentId { get; init; }
    public Guid ClientGeneratedId { get; init; }
    public string? ReceiptNumber { get; init; }
    public Guid InvoiceId { get; init; }
    public string? InvoiceNumber { get; init; }
    public Guid CustomerAccountId { get; init; }
    public decimal Amount { get; init; }
    public string? PaymentMethod { get; init; }
    public string? Reference { get; init; }
    public DateTime RecordedAt { get; init; }
    public Guid RecordedByStaffId { get; init; }
    public decimal InvoiceTotalAtPayment { get; init; }
    public decimal BalanceAfterPayment { get; init; }
    public bool IsLegacy { get; init; }

    public static PaymentReceiptResponse From(InvoicePayment payment) => new()
    {
        PaymentId = payment.Id,
        ClientGeneratedId = payment.ClientGeneratedId,
        ReceiptNumber = payment.ReceiptNumber,
        InvoiceId = payment.SaleInvoiceId,
        InvoiceNumber = payment.Invoice.InvoiceNumber,
        CustomerAccountId = payment.Invoice.CustomerAccountId,
        Amount = payment.Amount,
        PaymentMethod = payment.PaymentMethod?.ToString(),
        Reference = payment.Reference,
        RecordedAt = payment.RecordedAt,
        RecordedByStaffId = payment.RecordedByStaffId,
        InvoiceTotalAtPayment = payment.InvoiceTotalAtPayment,
        BalanceAfterPayment = payment.BalanceAfterPayment,
        IsLegacy = payment.IsLegacy
    };
}
