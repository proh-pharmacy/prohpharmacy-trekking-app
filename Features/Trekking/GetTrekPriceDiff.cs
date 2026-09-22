using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetTrekPriceDiff
{
    public class Query : IRequest<Result<PriceDiffResponse>>
    {
        public Guid TrekId { get; set; }
    }

    public class PriceDiffResponse
    {
        public Guid TrekId { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public bool SyncRequired { get; set; }
        public List<ProductDiff> Differences { get; set; } = [];
    }

    public class ProductDiff
    {
        public Guid StopId { get; set; }
        public int StopSequence { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public Guid StopProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal SnapshotBasicUnitPrice { get; set; }
        public decimal CatalogBasicUnitPrice { get; set; }
        public bool BasicPriceChanged { get; set; }
        public decimal? SnapshotPackagingUnitPrice { get; set; }
        public decimal? CatalogPackagingUnitPrice { get; set; }
        public bool PackagingPriceChanged { get; set; }
        public bool PackagingAdded { get; set; }
        public bool PackagingRemoved { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<PriceDiffResponse>>
    {
        public async Task<Result<PriceDiffResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Stops.OrderBy(s => s.Sequence))
                    .ThenInclude(s => s.CustomerAccount)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<PriceDiffResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            if (trip.Status == TrekStatus.Completed || trip.Status == TrekStatus.Cancelled)
                return Result.Success(new PriceDiffResponse
                {
                    TrekId = trip.Id,
                    TrekNumber = trip.TrekNumber,
                    SyncRequired = false,
                    Differences = []
                });

            var candidateStopProducts = trip.Stops
                .SelectMany(s => s.Products.Select(p => new { Stop = s, Product = p }))
                .Where(x => trip.Status != TrekStatus.InProgress || !x.Product.DeliveredAt.HasValue)
                .ToList();

            var productIds = candidateStopProducts
                .Select(x => x.Product.ProductId)
                .Distinct()
                .ToList();

            var catalogPrices = await db.Products
                .Where(p => productIds.Contains(p.Id))
                .AsNoTracking()
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            var regionMarkups = await db.RegionalMarkupRules
                .Where(r => r.RegionId == trip.RegionId &&
                            (r.ProductId == null || productIds.Contains(r.ProductId.Value)))
                .AsNoTracking()
                .ToDictionaryAsync(r => r.ProductId, r => r.MarkupPercentage, cancellationToken);

            var customerIds = candidateStopProducts.Select(x => x.Stop.CustomerAccountId).Distinct().ToList();
            var allCustomerMarkupRows = await db.CustomerMarkupRules
                .Where(r => customerIds.Contains(r.CustomerAccountId) &&
                            (r.ProductId == null || productIds.Contains(r.ProductId.Value)))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var customerMarkups = allCustomerMarkupRows
                .GroupBy(r => r.CustomerAccountId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.ProductId, r => r.MarkupPercentage));

            var diffs = new List<ProductDiff>();

            foreach (var x in candidateStopProducts)
            {
                var stop = x.Stop;
                var sp = x.Product;

                if (!catalogPrices.TryGetValue(sp.ProductId, out var cat)) continue;

                customerMarkups.TryGetValue(stop.CustomerAccountId, out var stopCustomerMarkups);
                stopCustomerMarkups ??= [];

                var resolvedBasic = ResolvePrice(cat.BasicUnitPrice, sp.ProductId, stopCustomerMarkups, regionMarkups);
                var basicChanged = sp.BasicUnitPrice != resolvedBasic;
                var hadPackaging = sp.PackagingUnitPrice.HasValue;
                var nowHasPackaging = cat.PackagingUnitId.HasValue;
                var packagingAdded = !hadPackaging && nowHasPackaging;
                var packagingRemoved = hadPackaging && !nowHasPackaging;
                decimal? resolvedPkg = nowHasPackaging
                    ? ResolvePrice(cat.PackagingUnitPrice!.Value, sp.ProductId, stopCustomerMarkups, regionMarkups)
                    : null;
                var packagingPriceChanged = hadPackaging && nowHasPackaging && sp.PackagingUnitPrice != resolvedPkg;

                if (!basicChanged && !packagingAdded && !packagingRemoved && !packagingPriceChanged)
                    continue;

                diffs.Add(new ProductDiff
                {
                    StopId = stop.Id,
                    StopSequence = stop.Sequence,
                    CustomerName = stop.CustomerAccount?.BusinessName ?? string.Empty,
                    StopProductId = sp.Id,
                    ProductName = sp.Product?.Name ?? string.Empty,
                    SnapshotBasicUnitPrice = sp.BasicUnitPrice,
                    CatalogBasicUnitPrice = resolvedBasic,
                    BasicPriceChanged = basicChanged,
                    SnapshotPackagingUnitPrice = sp.PackagingUnitPrice,
                    CatalogPackagingUnitPrice = resolvedPkg,
                    PackagingPriceChanged = packagingPriceChanged,
                    PackagingAdded = packagingAdded,
                    PackagingRemoved = packagingRemoved
                });
            }

            return Result.Success(new PriceDiffResponse
            {
                TrekId = trip.Id,
                TrekNumber = trip.TrekNumber,
                SyncRequired = diffs.Count > 0,
                Differences = diffs
            });
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

public class GetTrekPriceDiffEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/{trekId:guid}/price-diff", async (Guid trekId, ISender sender) =>
        {
            var result = await sender.Send(new GetTrekPriceDiff.Query { TrekId = trekId });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get price differences between trek stop products and current catalog")
        .WithDescription(
            "Compares each stop product's snapshotted prices against the current product catalog. " +
            "Returns only the products where a difference exists. " +
            "syncRequired is true when at least one difference is found. " +
            "Use this to show the user what will change before they confirm a sync.")
        .Produces<GetTrekPriceDiff.PriceDiffResponse>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
