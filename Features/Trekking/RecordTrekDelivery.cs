using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class RecordTrekDelivery
{
    public class Command : IRequest<Result<RecordResponse>>
    {
        public Guid TrekId { get; set; }
        public List<ProductRecord> Products { get; set; } = [];
        public List<StopInvoiceSync>? StopInvoices { get; set; }
    }

    public class ProductRecord
    {
        public Guid StopProductId { get; set; }
        public decimal? BasicQtyDelivered { get; set; }
        public decimal? PackagingQtyDelivered { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public decimal? AmtPaid { get; set; }
        public decimal? Balance { get; set; }
        public string? Notes { get; set; }
    }

    public class StopInvoiceSync
    {
        public Guid StopId { get; set; }
        public Guid? ClientGeneratedId { get; set; }
        public DateTime? RecordedAt { get; set; }
    }

    public class RecordResponse
    {
        public Guid TrekId { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Recorded { get; set; }
        public List<StopInvoiceResult> Invoices { get; set; } = [];
    }

    public class StopInvoiceResult
    {
        public Guid StopId { get; set; }
        public Guid InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Command, Result<RecordResponse>>
    {
        public async Task<Result<RecordResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (request.Products.Any(p => p.AmtPaid.HasValue || p.PaymentMethod.HasValue || p.Balance.HasValue))
                return Result.Failure<RecordResponse>(Error.BadRequest("Record payments using the invoice payments endpoint. Delivery requests must omit amtPaid, paymentMethod and balance."));
            if (request.Products.Any(p => p.BasicQtyDelivered < 0 || p.PackagingQtyDelivered < 0))
                return Result.Failure<RecordResponse>(Error.BadRequest("Delivered quantities cannot be negative."));

            if (request.Products.Any(p => !p.BasicQtyDelivered.HasValue && !p.PackagingQtyDelivered.HasValue) ||
                request.Products.Select(p => p.StopProductId).Distinct().Count() != request.Products.Count)
                return Result.Failure<RecordResponse>(Error.BadRequest("Provide delivered quantities for each distinct product. Use zero explicitly for goods not supplied."));

            var trip = await db.TrekkingTrips
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                .Include(t => t.Branch)
                .Include(t => t.Region)
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<RecordResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            if (trip.Status is not (TrekStatus.Scheduled or TrekStatus.InProgress))
                return Result.Failure<RecordResponse>(Error.BadRequest(
                    $"Deliveries can only be recorded on a Scheduled or InProgress trek. This trek is {trip.Status}."));

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
                    return Result.Failure<RecordResponse>(Error.BadRequest("A product does not belong to this trek."));

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

            return Result.Success(new RecordResponse
            {
                TrekId = trip.Id,
                TrekNumber = trip.TrekNumber,
                Status = trip.Status.ToString(),
                Recorded = recorded,
                Invoices = invoiceResults
            });
        }
    }

    internal static async Task<List<StopInvoiceResult>> GetInvoiceResultsAsync(
        AppDbContext db, IEnumerable<TrekkingTripStop> stops, CancellationToken ct)
    {
        var ids = stops.Select(s => s.Id).ToList();
        return await db.SaleInvoices.Where(i => ids.Contains(i.TrekkingTripStopId))
            .Select(i => new StopInvoiceResult
            {
                StopId = i.TrekkingTripStopId, InvoiceId = i.Id, InvoiceNumber = i.InvoiceNumber
            }).ToListAsync(ct);
    }

}

public class RecordTrekDeliveryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/{id:guid}/record", async (Guid id, RecordTrekDelivery.Command command, ISender sender) =>
        {
            command.TrekId = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Record delivery results for a trek (admin)")
        .WithDescription("Records delivery quantities and updates the existing stop invoice. Omit amtPaid, paymentMethod and balance; record each payment through the invoice payments endpoint. Completed or cancelled treks cannot be modified.")
        .Produces<RecordTrekDelivery.RecordResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
