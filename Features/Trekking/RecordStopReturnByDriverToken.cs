using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class RecordStopReturnByDriverToken
{
    public class Command : IRequest<Result<RecordStopReturn.ReturnResponse>>
    {
        public Guid Token { get; set; }
        public Guid StopId { get; set; }
        public Guid? SaleInvoiceId { get; set; }
        public Guid ProductId { get; set; }
        public decimal BasicQtyReturned { get; set; }
        public decimal? PackagingQtyReturned { get; set; }
        public PaymentMethod? RefundMethod { get; set; }
        public string? Reason { get; set; }
        public GpsInput? Gps { get; set; }

        public class GpsInput
        {
            public decimal Latitude { get; set; }
            public decimal Longitude { get; set; }
            public decimal AccuracyMetres { get; set; }
        }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.ProductId).NotEmpty();
            RuleFor(x => x.BasicQtyReturned).GreaterThan(0);
            RuleFor(x => x.PackagingQtyReturned).GreaterThan(0).When(x => x.PackagingQtyReturned.HasValue);
            RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
            RuleFor(x => x.Gps!.Latitude).InclusiveBetween(-90, 90).When(x => x.Gps is not null);
            RuleFor(x => x.Gps!.Longitude).InclusiveBetween(-180, 180).When(x => x.Gps is not null);
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator)
        : IRequestHandler<Command, Result<RecordStopReturn.ReturnResponse>>
    {
        public async Task<Result<RecordStopReturn.ReturnResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<RecordStopReturn.ReturnResponse>(Error.ValidationError(validation));

            var trip = await db.TrekkingTrips
                .Include(t => t.Stops.Where(s => s.Id == request.StopId))
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<RecordStopReturn.ReturnResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            if (trip.Status == TrekStatus.Completed || trip.Status == TrekStatus.Cancelled)
                return Result.Failure<RecordStopReturn.ReturnResponse>(Error.BadRequest($"Cannot record a return on a {trip.Status} trek."));

            var stop = trip.Stops.FirstOrDefault();
            if (stop is null)
                return Result.Failure<RecordStopReturn.ReturnResponse>(Error.CreateNotFoundError("Stop not found on this trek."));

            var invoiceQuery = db.SaleInvoices
                .Include(i => i.Stop)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.CustomerAccount)
                .AsQueryable();

            var invoice = request.SaleInvoiceId.HasValue
                ? await invoiceQuery.FirstOrDefaultAsync(i => i.Id == request.SaleInvoiceId.Value, cancellationToken)
                : await invoiceQuery.FirstOrDefaultAsync(i => i.TrekkingTripStopId == request.StopId, cancellationToken);

            if (invoice is null)
                return Result.Failure<RecordStopReturn.ReturnResponse>(Error.CreateNotFoundError(
                    request.SaleInvoiceId.HasValue
                        ? "Invoice not found."
                        : "No invoice exists for this stop yet. Record a delivery first."));

            if (invoice.CustomerAccountId != stop.CustomerAccountId)
                return Result.Failure<RecordStopReturn.ReturnResponse>(Error.BadRequest("Invoice does not belong to this stop's customer."));

            var lineItem = invoice.Stop.Products
                .FirstOrDefault(p => p.ProductId == request.ProductId);

            if (lineItem is null)
                return Result.Failure<RecordStopReturn.ReturnResponse>(Error.BadRequest("Product not found on this invoice."));

            var refundAmount = request.BasicQtyReturned * lineItem.BasicUnitPrice
                + (request.PackagingQtyReturned ?? 0) * (lineItem.PackagingUnitPrice ?? 0);

            var attributedStaffId = trip.SalesStaffId ?? trip.DriverStaffId;

            var ret = new TrekkingTripStopReturn
            {
                TrekkingTripStopId = request.StopId,
                SaleInvoiceId = invoice.Id,
                ProductId = request.ProductId,
                BasicQtyReturned = request.BasicQtyReturned,
                PackagingQtyReturned = request.PackagingQtyReturned,
                BasicUnitPrice = lineItem.BasicUnitPrice,
                PackagingUnitPrice = lineItem.PackagingUnitPrice,
                RefundAmount = refundAmount,
                RefundMethod = request.RefundMethod,
                Reason = request.Reason?.Trim(),
                ApprovalStatus = ReturnApprovalStatus.Pending,
                RecordedByStaffId = attributedStaffId,
                Latitude = request.Gps?.Latitude,
                Longitude = request.Gps?.Longitude,
                GpsAccuracyMetres = request.Gps?.AccuracyMetres,
                RecordedAt = DateTime.UtcNow
            };

            db.TrekkingTripStopReturns.Add(ret);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(RecordStopReturn.Handler.ToResponse(ret, invoice.InvoiceNumber, lineItem.Product.Name));
        }
    }
}

public class RecordStopReturnByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/stops/{stopId:guid}/returns",
            async (Guid token, Guid stopId, RecordStopReturnByDriverToken.Command command, ISender sender) =>
            {
                command.Token = token;
                command.StopId = stopId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Created($"api/v1/treks/driver/{token}/stops/{stopId}/returns/{result.Value.ReturnId}", result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Record a product return at a stop (driver portal)")
        .WithDescription("Online only. The return attaches to the current stop but may reference any invoice (including historical ones) whose customer matches the stop's customer. `saleInvoiceId` is optional — when omitted, the stop's own invoice is used (requires that a delivery has been recorded). Use `GET /treks/driver/{token}/stops/{stopId}/returnable-invoices` to list eligible invoices. Prices sourced from the referenced invoice. Return is Pending until admin approves.")
        .Produces<RecordStopReturn.ReturnResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
