using Carter;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Services.Invoicing;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking.Payments;

public sealed class RecordInvoicePayment : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/invoices/{invoiceId:guid}/payments", async (
            Guid invoiceId, InvoicePaymentRequest request, AppDbContext db, AuthProvider auth, CancellationToken ct) =>
        {
            if (!Guid.TryParse(auth.GetStaffId(), out var staffId))
                return Results.UnprocessableEntity(Error.BadRequest("Authenticated staff identity is missing."));
            var result = await new InvoicePaymentService(db).RecordAsync(invoiceId, request, staffId, null, ct);
            return result.IsFailure ? Results.UnprocessableEntity(result.Error) : Results.Ok(result.Value);
        }).WithTags("Trekking").RequireAuthorization()
          .WithSummary("Record an invoice payment and issue its receipt")
          .Produces<PaymentReceiptResponse>();

        app.MapPost("api/v1/treks/driver/{token:guid}/invoices/{invoiceId:guid}/payments", async (
            Guid token, Guid invoiceId, InvoicePaymentRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var trip = await db.TrekkingTrips.AsNoTracking().SingleOrDefaultAsync(t => t.DriverToken == token, ct);
            if (trip is null) return Results.NotFound(Error.CreateNotFoundError("Driver link not found."));
            if (trip.Status == Enums.TrekStatus.Cancelled)
                return Results.UnprocessableEntity(Error.BadRequest("This driver link belongs to a cancelled trek."));
            var result = await new InvoicePaymentService(db).RecordAsync(invoiceId, request,
                trip.SalesStaffId ?? trip.DriverStaffId, trip.RegionId, ct);
            return result.IsFailure ? Results.UnprocessableEntity(result.Error) : Results.Ok(result.Value);
        }).WithTags("Trekking").AllowAnonymous()
          .WithSummary("Record a payment against an invoice in the driver's region")
          .Produces<PaymentReceiptResponse>();
    }
}
