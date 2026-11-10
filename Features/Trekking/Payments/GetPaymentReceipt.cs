using Carter;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking.Payments;

public sealed class GetPaymentReceipt : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/payments/{paymentId:guid}/receipt", async (Guid paymentId, AppDbContext db, CancellationToken ct) =>
        {
            var payment = await db.InvoicePayments.Include(p => p.Invoice).AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == paymentId, ct);
            return payment is null ? Results.NotFound(Error.CreateNotFoundError("Payment not found."))
                : Results.Ok(PaymentReceiptResponse.From(payment));
        }).WithTags("Trekking").RequireAuthorization().Produces<PaymentReceiptResponse>();

        app.MapGet("api/v1/treks/driver/{token:guid}/payments/{paymentId:guid}/receipt", async (
            Guid token, Guid paymentId, AppDbContext db, CancellationToken ct) =>
        {
            var payment = await db.InvoicePayments.Include(p => p.Invoice).AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == paymentId && db.TrekkingTrips.Any(t =>
                    t.DriverToken == token && t.Status != Enums.TrekStatus.Cancelled &&
                    t.RegionId == p.Invoice.Stop.TrekkingTrip.RegionId), ct);
            return payment is null ? Results.NotFound(Error.CreateNotFoundError("Payment not found."))
                : Results.Ok(PaymentReceiptResponse.From(payment));
        }).WithTags("Trekking").AllowAnonymous().Produces<PaymentReceiptResponse>();
    }
}
