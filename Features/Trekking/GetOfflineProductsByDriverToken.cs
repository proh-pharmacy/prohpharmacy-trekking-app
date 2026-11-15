using prohpharmacy_trekking_app.Services.Pricing;
using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetOfflineProductsByDriverToken
{
    public class Query : IRequest<Result<OfflineProductsBundle>>
    {
        public Guid Token { get; set; }
        public DateTime? Since { get; set; }
        public bool All { get; set; }
    }

    public class OfflineProductsBundle
    {
        public List<OfflineProductItem> Products { get; set; } = [];
        // stopId → productId → customer-specific resolved price (only populated when different from region price)
        public Dictionary<string, Dictionary<string, OfflineStopProductPrice>> StopPriceOverrides { get; set; } = [];
    }

    public class OfflineProductItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? VehicleBasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public string BasicUnitName { get; set; } = string.Empty;
        public Guid BasicUnitId { get; set; }
        public string? PackagingUnitName { get; set; }
        public Guid? PackagingUnitId { get; set; }
        public bool IsActive { get; set; }
        public bool InVehicleCatalogue { get; set; }
    }

    public class OfflineStopProductPrice
    {
        public decimal BasicUnitPrice { get; set; }
        public decimal? VehicleBasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<OfflineProductsBundle>>
    {
        public async Task<Result<OfflineProductsBundle>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);
            if (trip is null)
                return Result.Failure<OfflineProductsBundle>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var catalogueProductIds = await db.VehicleProductStocks
                .Where(s => s.VehicleId == trip.VehicleId)
                .Select(s => s.ProductId)
                .ToListAsync(cancellationToken);
            var catalogueSet = new HashSet<Guid>(catalogueProductIds);

            var query = db.Products
                .Include(p => p.BasicUnit)
                .Include(p => p.PackagingUnit)
                .AsNoTracking();

            if (!request.All)
                query = query.Where(p => catalogueSet.Contains(p.Id));

            // Prices can change through stock uploads or deleted markup rules without
            // touching Product. Return the complete pricing bundle even with since,
            // so clients can replace (and clear) obsolete per-stop overrides.

            var products = await query.ToListAsync(cancellationToken);
            var productIds = products.Select(p => p.Id).ToList();

            var stops = await db.TrekkingTripStops.AsNoTracking()
                .Where(s => s.TrekkingTripId == trip.Id)
                .Select(s => new { s.Id, s.CustomerAccountId })
                .ToListAsync(cancellationToken);
            var pricing = await new TrekPriceReader(db).ReadAsync(trip.VehicleId, trip.RegionId,
                productIds, stops.Select(s => s.CustomerAccountId), cancellationToken);
            var productItems = products.Select(p => new OfflineProductItem
            {
                Id = p.Id, Name = p.Name, Description = p.Description,
                BasicUnitPrice = pricing.Basic(p), PackagingUnitPrice = pricing.Packaging(p),
                VehicleBasicUnitPrice = pricing.VehiclePrice(p.Id),
                BasicUnitName = p.BasicUnit?.Name ?? string.Empty, BasicUnitId = p.BasicUnitId,
                PackagingUnitName = p.PackagingUnit?.Name, PackagingUnitId = p.PackagingUnitId,
                IsActive = p.IsActive, InVehicleCatalogue = catalogueSet.Contains(p.Id)
            }).ToList();
            var stopPriceOverrides = new Dictionary<string, Dictionary<string, OfflineStopProductPrice>>();
            foreach (var stop in stops)
            {
                var overrides = new Dictionary<string, OfflineStopProductPrice>();
                foreach (var product in products)
                {
                    var basic = pricing.Basic(product, stop.CustomerAccountId);
                    var packaging = pricing.Packaging(product, stop.CustomerAccountId);
                    if (basic != pricing.Basic(product) || packaging != pricing.Packaging(product))
                        overrides[product.Id.ToString()] = new OfflineStopProductPrice
                        {
                            BasicUnitPrice = basic, PackagingUnitPrice = packaging,
                            VehicleBasicUnitPrice = pricing.VehiclePrice(product.Id)
                        };
                }
                if (overrides.Count > 0) stopPriceOverrides[stop.Id.ToString()] = overrides;
            }

            return Result.Success(new OfflineProductsBundle { Products = productItems, StopPriceOverrides = stopPriceOverrides });
        }


    }
}

public class GetOfflineProductsByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/offline/products",
            async (Guid token, DateTime? since, bool? all, ISender sender) =>
            {
                var result = await sender.Send(new GetOfflineProductsByDriverToken.Query { Token = token, Since = since, All = all ?? false });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get product catalogue for offline use (driver portal)")
        .WithDescription("Returns products scoped to the trek vehicle's warehouse catalogue by default (products with a VehicleProductStock record on the trek's vehicle, regardless of current quantity). Pass ?all=true to return the full catalogue. Every product carries an `inVehicleCatalogue` flag so the UI can mark non-vehicle items. The since parameter is accepted for compatibility; the complete current pricing bundle is returned so vehicle price and markup changes are included. Replace the cached stopPriceOverrides dictionary.")
        .Produces<GetOfflineProductsByDriverToken.OfflineProductsBundle>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
