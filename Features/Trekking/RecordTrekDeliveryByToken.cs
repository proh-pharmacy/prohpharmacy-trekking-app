using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class RecordTrekDeliveryByToken
{
    public class Command : IRequest<Result<RecordTrekDelivery.RecordResponse>>
    {
        public Guid Token { get; set; }
        public List<RecordTrekDelivery.ProductRecord> Products { get; set; } = [];
        public List<RecordTrekDelivery.StopInvoiceSync>? StopInvoices { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Command, Result<RecordTrekDelivery.RecordResponse>>
    {
        public async Task<Result<RecordTrekDelivery.RecordResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (request.Products.Any(p => p.AmtPaid.HasValue || p.PaymentMethod.HasValue || p.Balance.HasValue))
                return Result.Failure<RecordTrekDelivery.RecordResponse>(Error.BadRequest("Record payments using the invoice payments endpoint. Delivery requests must omit amtPaid, paymentMethod and balance."));
            if (request.Products.Any(p => p.BasicQtyDelivered < 0 || p.PackagingQtyDelivered < 0))
                return Result.Failure<RecordTrekDelivery.RecordResponse>(Error.BadRequest("Delivered quantities cannot be negative."));

            if (request.Products.Any(p => !p.BasicQtyDelivered.HasValue && !p.PackagingQtyDelivered.HasValue) ||
                request.Products.Select(p => p.StopProductId).Distinct().Count() != request.Products.Count)
                return Result.Failure<RecordTrekDelivery.RecordResponse>(Error.BadRequest("Provide delivered quantities for each distinct product. Use zero explicitly for goods not supplied."));

            var trip = await db.TrekkingTrips
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                .Include(t => t.Branch)
                .Include(t => t.Region)
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<RecordTrekDelivery.RecordResponse>(Error.CreateNotFoundError("Trek not found. The link may be invalid."));

            if (trip.Status is TrekStatus.Completed or TrekStatus.Cancelled)
                return Result.Failure<RecordTrekDelivery.RecordResponse>(Error.BadRequest("This trek is completed or cancelled and can no longer be modified."));

            if (trip.Status == TrekStatus.Scheduled)
            {
                trip.Status = TrekStatus.InProgress;
                trip.UpdatedAt = DateTime.UtcNow;
            }

            var productMap = trip.Stops
                .SelectMany(s => s.Products)
                .ToDictionary(p => p.Id);

            var productToStop = new Dictionary<Guid, TrekkingTripStop>();
            foreach (var stop in trip.Stops)
                foreach (var product in stop.Products)
                    productToStop[product.Id] = stop;

            var affectedStops = new HashSet<TrekkingTripStop>();
            var recorded = 0;
            foreach (var record in request.Products)
            {
                if (!productMap.TryGetValue(record.StopProductId, out var product))
                    return Result.Failure<RecordTrekDelivery.RecordResponse>(Error.BadRequest("A product does not belong to this trek."));

                product.BasicQtyDelivered = record.BasicQtyDelivered ?? product.BasicQtyDelivered ?? 0;
                product.PackagingQtyDelivered = record.PackagingQtyDelivered ?? product.PackagingQtyDelivered;

                product.Notes = record.Notes?.Trim();
                product.DeliveredAt = DateTime.UtcNow;

                if (productToStop.TryGetValue(record.StopProductId, out var affectedStop))
                    affectedStops.Add(affectedStop);

                recorded++;
            }

            trip.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            var invoiceResults = await RecordTrekDelivery.GetInvoiceResultsAsync(db, affectedStops, cancellationToken);

            return Result.Success(new RecordTrekDelivery.RecordResponse
            {
                TrekId = trip.Id,
                TrekNumber = trip.TrekNumber,
                Status = trip.Status.ToString(),
                Recorded = recorded,
                Invoices = invoiceResults
            });
        }
    }
}

public class RecordTrekDeliveryByTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/record", async (Guid token, RecordTrekDeliveryByToken.Command command, ISender sender) =>
        {
            command.Token = token;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Record delivery results via driver token")
        .WithDescription("Records delivery quantities and updates the existing stop invoice. Omit amtPaid, paymentMethod and balance; record each payment through the invoice payments endpoint. Completed or cancelled treks cannot be modified.")
        .Produces<RecordTrekDelivery.RecordResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
