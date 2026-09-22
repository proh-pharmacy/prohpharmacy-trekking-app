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

            var regionMarkups = await db.RegionalMarkupRules
                .Where(r => r.RegionId == trip.RegionId &&
                            (r.ProductId == null || stopProductIds.Contains(r.ProductId.Value)))
                .AsNoTracking()
                .ToDictionaryAsync(r => r.ProductId, r => r.MarkupPercentage, cancellationToken);

            var customerIds = trip.Stops.Select(s => s.CustomerAccountId).Distinct().ToList();
            var allCustomerMarkupRows = await db.CustomerMarkupRules
                .Where(r => customerIds.Contains(r.CustomerAccountId) &&
                            (r.ProductId == null || stopProductIds.Contains(r.ProductId.Value)))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var customerMarkups = allCustomerMarkupRows
                .GroupBy(r => r.CustomerAccountId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.ProductId, r => r.MarkupPercentage));

            var response = new SyncPricesResponse
            {
                TrekId = trip.Id,
                TrekNumber = trip.TrekNumber
            };

            var anyChanged = false;

            foreach (var stop in trip.Stops)
            {
                customerMarkups.TryGetValue(stop.CustomerAccountId, out var stopCustomerMarkups);
                stopCustomerMarkups ??= [];

                foreach (var sp in stop.Products.Where(p => trip.Status != TrekStatus.InProgress || !p.DeliveredAt.HasValue))
                {
                    if (!catalogProducts.TryGetValue(sp.ProductId, out var catalog))
                        continue;

                    var changed = false;

                    var resolvedBasic = ResolvePrice(catalog.BasicUnitPrice, sp.ProductId, stopCustomerMarkups, regionMarkups);
                    if (sp.BasicUnitPrice != resolvedBasic)
                    {
                        response.Changes.Add($"{catalog.Name}: basic price {sp.BasicUnitPrice:F2} → {resolvedBasic:F2}");
                        sp.BasicUnitPrice = resolvedBasic;
                        changed = true;
                    }

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
                        var resolvedPkg = ResolvePrice(catalog.PackagingUnitPrice!.Value, sp.ProductId, stopCustomerMarkups, regionMarkups);
                        response.Changes.Add($"{catalog.Name}: packaging unit added at {resolvedPkg:F2}");
                        sp.PackagingUnitPrice = resolvedPkg;
                        response.PackagingAdded++;
                        changed = true;
                    }
                    else if (hadPackaging && nowHasPackaging)
                    {
                        var resolvedPkg = ResolvePrice(catalog.PackagingUnitPrice!.Value, sp.ProductId, stopCustomerMarkups, regionMarkups);
                        if (sp.PackagingUnitPrice != resolvedPkg)
                        {
                            response.Changes.Add($"{catalog.Name}: packaging price {sp.PackagingUnitPrice:F2} → {resolvedPkg:F2}");
                            sp.PackagingUnitPrice = resolvedPkg;
                            changed = true;
                        }
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

        private static decimal ApplyMarkup(decimal price, decimal pct) =>
            Math.Round(price * (1 + pct / 100m), 2);

        private static decimal ResolvePrice(
            decimal basePrice,
            Guid productId,
            Dictionary<Guid?, decimal> customerMarkups,
            Dictionary<Guid?, decimal> regionMarkups)
        {
            if (customerMarkups.TryGetValue(productId, out var cm)) return ApplyMarkup(basePrice, cm);
            if (customerMarkups.TryGetValue(null, out var cw)) return ApplyMarkup(basePrice, cw);
            if (regionMarkups.TryGetValue(productId, out var rm)) return ApplyMarkup(basePrice, rm);
            if (regionMarkups.TryGetValue(null, out var rw)) return ApplyMarkup(basePrice, rw);
            return basePrice;
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
