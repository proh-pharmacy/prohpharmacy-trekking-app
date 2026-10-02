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

public static class RecordCustomerReturnsByDriverToken
{
    public class Command : IRequest<Result<BatchReturnResponse>>
    {
        public Guid Token { get; set; }
        public Guid CustomerAccountId { get; set; }
        public Guid SaleInvoiceId { get; set; }
        public List<ItemInput> Items { get; set; } = [];
        public GpsInput? Gps { get; set; }

        public class ItemInput
        {
            public Guid ProductId { get; set; }
            public decimal BasicQtyReturned { get; set; }
            public decimal? PackagingQtyReturned { get; set; }
            public PaymentMethod? RefundMethod { get; set; }
            public string? Reason { get; set; }
        }

        public class GpsInput
        {
            public decimal Latitude { get; set; }
            public decimal Longitude { get; set; }
            public decimal AccuracyMetres { get; set; }
        }
    }

    public class BatchReturnResponse
    {
        public Guid StopId { get; set; }
        public bool StopWasAutoAdded { get; set; }
        public List<RecordStopReturn.ReturnResponse> Returns { get; set; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.CustomerAccountId).NotEmpty();
            RuleFor(x => x.SaleInvoiceId).NotEmpty();
            RuleFor(x => x.Items).NotEmpty().WithMessage("At least one return item is required.");
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ProductId).NotEmpty();
                item.RuleFor(i => i.BasicQtyReturned).GreaterThan(0);
                item.RuleFor(i => i.PackagingQtyReturned).GreaterThan(0).When(i => i.PackagingQtyReturned.HasValue);
                item.RuleFor(i => i.Reason).MaximumLength(500).When(i => i.Reason is not null);
            });
            RuleFor(x => x.Gps!.Latitude).InclusiveBetween(-90, 90).When(x => x.Gps is not null);
            RuleFor(x => x.Gps!.Longitude).InclusiveBetween(-180, 180).When(x => x.Gps is not null);
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator)
        : IRequestHandler<Command, Result<BatchReturnResponse>>
    {
        public async Task<Result<BatchReturnResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<BatchReturnResponse>(Error.ValidationError(validation));

            var trip = await db.TrekkingTrips
                .Include(t => t.Stops)
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<BatchReturnResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            if (trip.Status == TrekStatus.Completed || trip.Status == TrekStatus.Cancelled)
                return Result.Failure<BatchReturnResponse>(Error.BadRequest($"Cannot record a return on a {trip.Status} trek."));

            var invoice = await db.SaleInvoices
                .Include(i => i.Stop)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                .FirstOrDefaultAsync(i => i.Id == request.SaleInvoiceId, cancellationToken);

            if (invoice is null)
                return Result.Failure<BatchReturnResponse>(Error.CreateNotFoundError("Invoice not found."));

            if (invoice.CustomerAccountId != request.CustomerAccountId)
                return Result.Failure<BatchReturnResponse>(Error.BadRequest("Invoice does not belong to the selected customer."));

            var stop = trip.Stops.FirstOrDefault(s => s.CustomerAccountId == request.CustomerAccountId);
            var stopWasAutoAdded = false;

            if (stop is null)
            {
                var nextSequence = trip.Stops.Count == 0 ? 1 : trip.Stops.Max(s => s.Sequence) + 1;
                stop = new TrekkingTripStop
                {
                    TrekkingTripId = trip.Id,
                    CustomerAccountId = request.CustomerAccountId,
                    Sequence = nextSequence,
                    IsWalkIn = true
                };
                db.TrekkingTripStops.Add(stop);
                stopWasAutoAdded = true;

                if (trip.Status == TrekStatus.Scheduled)
                {
                    trip.Status = TrekStatus.InProgress;
                    trip.UpdatedAt = DateTime.UtcNow;
                }
            }

            var attributedStaffId = trip.SalesStaffId ?? trip.DriverStaffId;
            var createdReturns = new List<TrekkingTripStopReturn>();
            var productNames = new Dictionary<Guid, string>();

            foreach (var item in request.Items)
            {
                var lineItem = invoice.Stop.Products.FirstOrDefault(p => p.ProductId == item.ProductId);
                if (lineItem is null)
                    return Result.Failure<BatchReturnResponse>(Error.BadRequest($"Product {item.ProductId} not found on this invoice."));

                var refundAmount = item.BasicQtyReturned * lineItem.BasicUnitPrice
                    + (item.PackagingQtyReturned ?? 0) * (lineItem.PackagingUnitPrice ?? 0);

                var ret = new TrekkingTripStopReturn
                {
                    TrekkingTripStopId = stop.Id,
                    SaleInvoiceId = invoice.Id,
                    ProductId = item.ProductId,
                    BasicQtyReturned = item.BasicQtyReturned,
                    PackagingQtyReturned = item.PackagingQtyReturned,
                    BasicUnitPrice = lineItem.BasicUnitPrice,
                    PackagingUnitPrice = lineItem.PackagingUnitPrice,
                    RefundAmount = refundAmount,
                    RefundMethod = item.RefundMethod,
                    Reason = item.Reason?.Trim(),
                    ApprovalStatus = ReturnApprovalStatus.Pending,
                    RecordedByStaffId = attributedStaffId,
                    Latitude = request.Gps?.Latitude,
                    Longitude = request.Gps?.Longitude,
                    GpsAccuracyMetres = request.Gps?.AccuracyMetres,
                    RecordedAt = DateTime.UtcNow
                };

                db.TrekkingTripStopReturns.Add(ret);
                createdReturns.Add(ret);
                productNames[item.ProductId] = lineItem.Product.Name;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new BatchReturnResponse
            {
                StopId = stop.Id,
                StopWasAutoAdded = stopWasAutoAdded,
                Returns = createdReturns
                    .Select(r => RecordStopReturn.Handler.ToResponse(r, invoice.InvoiceNumber, productNames[r.ProductId]))
                    .ToList()
            });
        }
    }
}

public class RecordCustomerReturnsByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/returns",
            async (Guid token, RecordCustomerReturnsByDriverToken.Command command, ISender sender) =>
            {
                command.Token = token;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Created($"api/v1/treks/driver/{token}/stops/{result.Value.StopId}", result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Record one or more returns for a customer (driver portal)")
        .WithDescription("Customer-focused batch return. The driver selects a customer, an invoice (any of the customer's invoices, including historical ones), and one or more products with quantities. All returns attach to the customer's stop on the current trek — the stop is auto-added as a walk-in if the customer is not already on the trek. Returns are Pending until admin approves.")
        .Produces<RecordCustomerReturnsByDriverToken.BatchReturnResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
