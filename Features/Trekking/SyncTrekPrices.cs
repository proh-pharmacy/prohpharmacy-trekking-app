using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class SyncTrekPrices
{
    public class Command : IRequest<Result<SyncPricesResponse>>
    {
        public Guid TrekId { get; set; }
    }

    public class SyncPricesResponse
    {
        public Guid TrekId { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public int ProductsUpdated { get; set; }
        public int PackagingAdded { get; set; }
        public int PackagingRemoved { get; set; }
        public List<string> Changes { get; set; } = [];
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Command, Result<SyncPricesResponse>>
    {
        public async Task<Result<SyncPricesResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<SyncPricesResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            if (trip.Status == TrekStatus.Completed || trip.Status == TrekStatus.Cancelled)
                return Result.Failure<SyncPricesResponse>(Error.BadRequest(
                    $"Cannot sync prices on a {trip.Status} trek."));

            var stopProductIds = trip.Stops
                .SelectMany(s => s.Products)
                .Select(p => p.ProductId)
                .Distinct()
                .ToList();

            var catalogProducts = await db.Products
                .Where(p => stopProductIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            var response = new SyncPricesResponse
            {
                TrekId = trip.Id,
                TrekNumber = trip.TrekNumber
            };

            var anyChanged = false;

            foreach (var stop in trip.Stops)
            {
                foreach (var sp in stop.Products)
                {
                    if (!catalogProducts.TryGetValue(sp.ProductId, out var catalog))
                        continue;

                    var changed = false;

                    // Basic price
                    if (sp.BasicUnitPrice != catalog.BasicUnitPrice)
                    {
                        response.Changes.Add($"{catalog.Name}: basic price {sp.BasicUnitPrice:F2} → {catalog.BasicUnitPrice:F2}");
                        sp.BasicUnitPrice = catalog.BasicUnitPrice;
                        changed = true;
                    }

                    // Packaging configuration
                    var hadPackaging = sp.PackagingUnitPrice.HasValue;
                    var nowHasPackaging = catalog.PackagingUnitId.HasValue;

                    if (hadPackaging && !nowHasPackaging)
                    {
                        response.Changes.Add($"{catalog.Name}: packaging unit removed — clearing packaging price and quantities");
                        sp.PackagingUnitPrice = null;
                        sp.PlannedPackagingQuantity = null;
                        sp.PackagingQtyDelivered = null;
                        response.PackagingRemoved++;
                        changed = true;
                    }
                    else if (!hadPackaging && nowHasPackaging)
                    {
                        response.Changes.Add($"{catalog.Name}: packaging unit added at {catalog.PackagingUnitPrice:F2}");
                        sp.PackagingUnitPrice = catalog.PackagingUnitPrice;
                        response.PackagingAdded++;
                        changed = true;
                    }
                    else if (hadPackaging && nowHasPackaging && sp.PackagingUnitPrice != catalog.PackagingUnitPrice)
                    {
                        response.Changes.Add($"{catalog.Name}: packaging price {sp.PackagingUnitPrice:F2} → {catalog.PackagingUnitPrice:F2}");
                        sp.PackagingUnitPrice = catalog.PackagingUnitPrice;
                        changed = true;
                    }

                    if (changed)
                    {
                        sp.AmountDue = (sp.PlannedBasicQuantity * sp.BasicUnitPrice)
                                     + ((sp.PlannedPackagingQuantity ?? 0) * (sp.PackagingUnitPrice ?? 0));
                        response.ProductsUpdated++;
                        anyChanged = true;
                    }
                }
            }

            if (anyChanged)
            {
                trip.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            return Result.Success(response);
        }
    }
}

public class SyncTrekPricesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/{trekId:guid}/sync-prices", async (Guid trekId, ISender sender) =>
        {
            var result = await sender.Send(new SyncTrekPrices.Command { TrekId = trekId });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Sync product prices from catalog to trek stop products")
        .WithDescription(
            "Re-snapshots all stop product prices from the current product catalog. " +
            "Updates BasicUnitPrice and PackagingUnitPrice on every stop product. " +
            "If a product's packaging unit was removed from the catalog, the stop product's packaging price, " +
            "planned packaging quantity, and delivered packaging quantity are all cleared. " +
            "If a product now has a packaging unit it previously lacked, the packaging price is added. " +
            "AmountDue is recalculated for every affected stop product. " +
            "Allowed on Draft, Scheduled, and InProgress treks. Blocked on Completed and Cancelled.")
        .Produces<SyncTrekPrices.SyncPricesResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
