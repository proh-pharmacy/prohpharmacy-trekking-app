using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Features.Trekking.Payments;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Services.Invoicing;

public sealed class InvoicePaymentService(AppDbContext db)
{
    public async Task<Result<PaymentReceiptResponse>> RecordAsync(Guid invoiceId, InvoicePaymentRequest request,
        Guid staffId, Guid? driverRegionId, CancellationToken ct)
    {
        if (request.ClientGeneratedId == Guid.Empty || request.Amount <= 0 ||
            decimal.Round(request.Amount, 2) != request.Amount || request.Amount >= 1_000_000_000_000m ||
            !request.PaymentMethod.HasValue || !Enum.IsDefined(request.PaymentMethod.Value) ||
            request.PaymentMethod == PaymentMethod.Credit || request.Reference?.Length > 200 ||
            (request.RecordedAt.HasValue && (request.RecordedAt.Value.Kind != DateTimeKind.Utc || request.RecordedAt > DateTime.UtcNow.AddMinutes(5))))
            return Result.Failure<PaymentReceiptResponse>(Error.BadRequest(
                "Provide a clientGeneratedId, a positive amount with at most two decimal places, and a payment method other than Credit. recordedAt must be UTC and not in the future; reference must be at most 200 characters."));

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        var scope = await db.SaleInvoices.Where(i => i.Id == invoiceId &&
                (!driverRegionId.HasValue || i.Stop.TrekkingTrip.RegionId == driverRegionId))
            .Select(i => new { i.TrekkingTripId }).SingleOrDefaultAsync(ct);
        if (scope is null)
            return Result.Failure<PaymentReceiptResponse>(Error.CreateNotFoundError("Invoice not found."));

        // Serialize requests before loading the balance and checking idempotency.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TrekkingTrips\" WHERE \"Id\" = {scope.TrekkingTripId} FOR UPDATE", ct);
        var previous = await db.InvoicePayments.Include(p => p.Invoice)
            .SingleOrDefaultAsync(p => p.ClientGeneratedId == request.ClientGeneratedId, ct);
        if (previous is not null)
        {
            if (previous.SaleInvoiceId != invoiceId || previous.Amount != request.Amount ||
                previous.PaymentMethod != request.PaymentMethod || previous.Reference != request.Reference?.Trim() ||
                (request.RecordedAt.HasValue && previous.RecordedAt != request.RecordedAt.Value))
                return Result.Failure<PaymentReceiptResponse>(Error.Conflict("This payment identifier was already used with different details."));
            return Result.Success(PaymentReceiptResponse.From(previous));
        }

        var invoice = await db.SaleInvoices.Include(i => i.Stop).ThenInclude(s => s.TrekkingTrip)
            .SingleAsync(i => i.Id == invoiceId, ct);
        await db.Entry(invoice).ReloadAsync(ct);
        if (invoice.Stop.TrekkingTrip.Status == TrekStatus.Draft)
            return Result.Failure<PaymentReceiptResponse>(Error.BadRequest(
                "A Draft trek has not issued invoices and cannot receive payments."));
        if (invoice.Status == SaleInvoiceStatus.Voided || invoice.Stop.TrekkingTrip.Status == TrekStatus.Cancelled)
            return Result.Failure<PaymentReceiptResponse>(Error.BadRequest("A voided invoice cannot receive payments."));
        if (request.Amount > invoice.Balance)
            return Result.Failure<PaymentReceiptResponse>(Error.BadRequest("Payment exceeds the invoice's outstanding balance."));

        var payment = new InvoicePayment
        {
            Invoice = invoice,
            SaleInvoiceId = invoice.Id,
            ClientGeneratedId = request.ClientGeneratedId,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            Reference = request.Reference?.Trim(),
            RecordedAt = request.RecordedAt ?? DateTime.UtcNow,
            RecordedByStaffId = staffId
        };
        payment.ReceiptNumber = $"RCP-{payment.Id:N}".ToUpperInvariant();
        db.InvoicePayments.Add(payment);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Result.Success(PaymentReceiptResponse.From(payment));
    }
}
