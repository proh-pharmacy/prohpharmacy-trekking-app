using prohpharmacy_trekking_app.Services.Pricing;
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

            var pricing = await new TrekPriceReader(db).ReadAsync(trip.VehicleId, trip.RegionId,
                productIds, trip.Stops.Select(s => s.CustomerAccountId), cancellationToken);

            var diffs = new List<ProductDiff>();

            foreach (var x in candidateStopProducts)
            {
                var stop = x.Stop;
                var sp = x.Product;

                if (!catalogPrices.TryGetValue(sp.ProductId, out var cat)) continue;



                var resolvedBasic = pricing.Basic(cat, stop.CustomerAccountId);
                var basicChanged = !sp.UsesVehicleStockPrice && !pricing.HasVehiclePrice(sp.ProductId) &&
                    sp.BasicUnitPrice != resolvedBasic;
                var hadPackaging = sp.PackagingUnitPrice.HasValue;
                var nowHasPackaging = cat.PackagingUnitId.HasValue;
                var packagingAdded = !hadPackaging && nowHasPackaging;
                var packagingRemoved = hadPackaging && !nowHasPackaging;
                decimal? resolvedPkg = nowHasPackaging
                    ? pricing.Packaging(cat, stop.CustomerAccountId)
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
            "Vehicle-stock basic prices are excluded from global price comparisons; packaging is still compared. " +
            "Returns only the products where a difference exists. " +
            "syncRequired is true when at least one difference is found. " +
            "Use this to show the user what will change before they confirm a sync.")
        .Produces<GetTrekPriceDiff.PriceDiffResponse>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
