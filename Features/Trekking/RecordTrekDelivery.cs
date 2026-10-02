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
            var trip = await db.TrekkingTrips
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                .Include(t => t.Branch)
                .Include(t => t.Region)
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<RecordResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            if (trip.Status == TrekStatus.Completed)
                return Result.Failure<RecordResponse>(Error.BadRequest("This trek is completed and can no longer be modified."));

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
                if (!productMap.TryGetValue(record.StopProductId, out var product)) continue;

                product.BasicQtyDelivered = record.BasicQtyDelivered;
                product.PackagingQtyDelivered = record.PackagingQtyDelivered;
                product.PaymentMethod = record.PaymentMethod;

                product.AmountDue = (record.BasicQtyDelivered ?? 0) * product.BasicUnitPrice
                                  + (record.PackagingQtyDelivered ?? 0) * (product.PackagingUnitPrice ?? 0);

                if (record.AmtPaid is null && (record.BasicQtyDelivered > 0 || record.PackagingQtyDelivered > 0))
                {
                    product.AmtPaid = product.AmountDue;
                    product.Balance = 0;
                }
                else
                {
                    product.AmtPaid = record.AmtPaid;
                    product.Balance = record.Balance;
                }

                product.Notes = record.Notes?.Trim();
                product.DeliveredAt = DateTime.UtcNow;

                if (productToStop.TryGetValue(record.StopProductId, out var affectedStop))
                    affectedStops.Add(affectedStop);

                recorded++;
            }

            trip.UpdatedAt = DateTime.UtcNow;

            var stopSyncMap = request.StopInvoices?
                .ToDictionary(s => s.StopId) ?? new Dictionary<Guid, StopInvoiceSync>();

            var invoiceResults = await BuildInvoicesAsync(db, trip, affectedStops, stopSyncMap, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

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

    internal static async Task<List<StopInvoiceResult>> BuildInvoicesAsync(
        AppDbContext db,
        TrekkingTrip trip,
        IEnumerable<TrekkingTripStop> affectedStops,
        Dictionary<Guid, StopInvoiceSync> stopSyncMap,
        CancellationToken ct)
    {
        var stopIds = affectedStops.Select(s => s.Id).ToList();
        if (stopIds.Count == 0) return [];

        var existingByStopId = await db.SaleInvoices
            .Where(i => stopIds.Contains(i.TrekkingTripStopId))
            .ToDictionaryAsync(i => i.TrekkingTripStopId, ct);

        var clientIds = stopSyncMap.Values
            .Where(s => s.ClientGeneratedId.HasValue)
            .Select(s => s.ClientGeneratedId!.Value)
            .ToList();

        var existingByClientId = clientIds.Count > 0
            ? await db.SaleInvoices
                .Where(i => i.ClientGeneratedId != null && clientIds.Contains(i.ClientGeneratedId!.Value))
                .ToDictionaryAsync(i => i.ClientGeneratedId!.Value, ct)
            : new Dictionary<Guid, SaleInvoice>();

        var scopeId = trip.BranchId ?? trip.RegionId;
        var scopeType = trip.BranchId.HasValue ? "Branch" : "Region";
        var scopeCode = (trip.BranchId.HasValue ? trip.Branch?.Code : trip.Region?.Code) ?? "GH";

        var results = new List<StopInvoiceResult>();

        foreach (var stop in affectedStops)
        {
            var deliveredProducts = stop.Products
                .Where(p => p.DeliveredAt.HasValue || p.BasicQtyDelivered.HasValue)
                .ToList();

            if (deliveredProducts.Count == 0) continue;

            var totalAmount = deliveredProducts.Sum(p =>
                (p.BasicQtyDelivered ?? 0) * p.BasicUnitPrice +
                (p.PackagingQtyDelivered ?? 0) * (p.PackagingUnitPrice ?? 0));
            var totalPaid = deliveredProducts.Sum(p => p.AmtPaid ?? 0);
            var balance = totalAmount - totalPaid;

            var status = balance <= 0 ? SaleInvoiceStatus.Paid
                       : totalPaid > 0 ? SaleInvoiceStatus.PartiallyPaid
                       : SaleInvoiceStatus.Issued;

            stopSyncMap.TryGetValue(stop.Id, out var syncData);

            SaleInvoice? invoice = null;

            if (syncData?.ClientGeneratedId.HasValue == true)
                existingByClientId.TryGetValue(syncData.ClientGeneratedId!.Value, out invoice);

            invoice ??= existingByStopId.GetValueOrDefault(stop.Id);

            if (invoice is not null)
            {
                invoice.TotalAmount = totalAmount;
                invoice.TotalPaid = totalPaid;
                invoice.Balance = balance;
                invoice.Status = status;
                invoice.UpdatedAt = DateTime.UtcNow;

                results.Add(new StopInvoiceResult
                {
                    StopId = stop.Id,
                    InvoiceId = invoice.Id,
                    InvoiceNumber = invoice.InvoiceNumber
                });
                continue;
            }

            var seq = (await db.Database.SqlQueryRaw<long>(
                """
                INSERT INTO "InvoiceNumberTrackers" ("ScopeId", "ScopeType", "LastSequence")
                VALUES ({0}, {1}, 1)
                ON CONFLICT ("ScopeId", "ScopeType") DO UPDATE
                SET "LastSequence" = "InvoiceNumberTrackers"."LastSequence" + 1
                RETURNING "LastSequence" AS "Value"
                """, scopeId, scopeType).ToListAsync(ct)).First();

            var invoiceNumber = $"INV-{scopeCode.ToUpper()}{seq:D5}";

            var newInvoice = new SaleInvoice
            {
                InvoiceNumber = invoiceNumber,
                TrekkingTripStopId = stop.Id,
                TrekkingTripId = trip.Id,
                CustomerAccountId = stop.CustomerAccountId,
                TotalAmount = totalAmount,
                TotalPaid = totalPaid,
                Balance = balance,
                Status = status,
                ClientGeneratedId = syncData?.ClientGeneratedId,
                CreatedOffline = syncData?.ClientGeneratedId.HasValue == true,
                IssuedAt = syncData?.RecordedAt ?? DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            db.SaleInvoices.Add(newInvoice);

            results.Add(new StopInvoiceResult
            {
                StopId = stop.Id,
                InvoiceId = newInvoice.Id,
                InvoiceNumber = invoiceNumber
            });
        }

        return results;
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
        .WithDescription("Allows an admin to enter delivery quantities, payment method, amount paid, and balance for each product in the trek. Rejected if the trek is already Completed. Automatically creates or updates a sale invoice per affected stop.")
        .Produces<RecordTrekDelivery.RecordResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
