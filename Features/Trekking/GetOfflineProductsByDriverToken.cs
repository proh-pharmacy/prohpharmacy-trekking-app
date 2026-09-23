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
        public decimal? PackagingUnitPrice { get; set; }
        public string BasicUnitName { get; set; } = string.Empty;
        public Guid BasicUnitId { get; set; }
        public string? PackagingUnitName { get; set; }
        public Guid? PackagingUnitId { get; set; }
        public bool IsActive { get; set; }
    }

    public class OfflineStopProductPrice
    {
        public decimal BasicUnitPrice { get; set; }
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

            var query = db.Products
                .Include(p => p.BasicUnit)
                .Include(p => p.PackagingUnit)
                .AsNoTracking();

            if (request.Since.HasValue)
                query = query.Where(p => p.CreatedAt >= request.Since.Value || (p.UpdatedAt.HasValue && p.UpdatedAt >= request.Since.Value));

            var products = await query.ToListAsync(cancellationToken);
            var productIds = products.Select(p => p.Id).ToList();

            var regionMarkupRules = await db.RegionalMarkupRules
                .Where(r => r.RegionId == trip.RegionId &&
                            (r.ProductId == null || productIds.Contains(r.ProductId.Value)))
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            var regionProductMarkups = regionMarkupRules
                .Where(r => r.ProductId.HasValue)
                .ToDictionary(r => r.ProductId!.Value, r => r.MarkupPercentage);
            var regionWideMarkup = regionMarkupRules
                .FirstOrDefault(r => !r.ProductId.HasValue)?.MarkupPercentage;

            var productItems = products.Select(p => new OfflineProductItem
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                BasicUnitPrice = ResolveRegionPrice(p.BasicUnitPrice, p.Id, regionProductMarkups, regionWideMarkup),
                PackagingUnitPrice = p.PackagingUnitPrice.HasValue
                    ? ResolveRegionPrice(p.PackagingUnitPrice.Value, p.Id, regionProductMarkups, regionWideMarkup)
                    : null,
                BasicUnitName = p.BasicUnit?.Name ?? string.Empty,
                BasicUnitId = p.BasicUnitId,
                PackagingUnitName = p.PackagingUnit?.Name,
                PackagingUnitId = p.PackagingUnitId,
                IsActive = p.IsActive
            }).ToList();

            // Build per-stop price overrides for stops whose customer has markup rules
            var stops = await db.TrekkingTripStops
                .Where(s => s.TrekkingTripId == trip.Id)
                .Select(s => new { s.Id, s.CustomerAccountId })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var customerIds = stops.Select(s => s.CustomerAccountId).Distinct().ToList();
            var customerMarkupRules = await db.CustomerMarkupRules
                .Where(r => customerIds.Contains(r.CustomerAccountId) &&
                            (r.ProductId == null || productIds.Contains(r.ProductId.Value)))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var customerProductMarkups = customerMarkupRules
                .Where(r => r.ProductId.HasValue)
                .GroupBy(r => r.CustomerAccountId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.ProductId!.Value, r => r.MarkupPercentage));
            var customerWideMarkups = customerMarkupRules
                .Where(r => !r.ProductId.HasValue)
                .ToDictionary(r => r.CustomerAccountId, r => r.MarkupPercentage);
            var customersWithRules = new HashSet<Guid>(customerMarkupRules.Select(r => r.CustomerAccountId));

            var stopPriceOverrides = new Dictionary<string, Dictionary<string, OfflineStopProductPrice>>();
            foreach (var stop in stops)
            {
                if (!customersWithRules.Contains(stop.CustomerAccountId)) continue;

                customerProductMarkups.TryGetValue(stop.CustomerAccountId, out var cProductMarkups);
                var cWideMarkup = customerWideMarkups.TryGetValue(stop.CustomerAccountId, out var cw) ? (decimal?)cw : null;

                var stopOverrides = new Dictionary<string, OfflineStopProductPrice>();
                foreach (var p in products)
                {
                    var basic = ResolveFullPrice(p.BasicUnitPrice, p.Id, cProductMarkups ?? [], cWideMarkup, regionProductMarkups, regionWideMarkup);
                    var packaging = p.PackagingUnitPrice.HasValue
                        ? ResolveFullPrice(p.PackagingUnitPrice.Value, p.Id, cProductMarkups ?? [], cWideMarkup, regionProductMarkups, regionWideMarkup)
                        : (decimal?)null;

                    var regionBasic = ResolveRegionPrice(p.BasicUnitPrice, p.Id, regionProductMarkups, regionWideMarkup);
                    var regionPackaging = p.PackagingUnitPrice.HasValue
                        ? ResolveRegionPrice(p.PackagingUnitPrice.Value, p.Id, regionProductMarkups, regionWideMarkup)
                        : (decimal?)null;

                    if (basic != regionBasic || packaging != regionPackaging)
                        stopOverrides[p.Id.ToString()] = new OfflineStopProductPrice { BasicUnitPrice = basic, PackagingUnitPrice = packaging };
                }

                if (stopOverrides.Count > 0)
                    stopPriceOverrides[stop.Id.ToString()] = stopOverrides;
            }

            return Result.Success(new OfflineProductsBundle { Products = productItems, StopPriceOverrides = stopPriceOverrides });
        }

        private static decimal ApplyMarkup(decimal price, decimal pct) =>
            Math.Round(price * (1 + pct / 100m), 2);

        private static decimal ResolveRegionPrice(decimal basePrice, Guid productId,
            Dictionary<Guid, decimal> regionProductMarkups, decimal? regionWideMarkup)
        {
            if (regionProductMarkups.TryGetValue(productId, out var rm)) return ApplyMarkup(basePrice, rm);
            if (regionWideMarkup.HasValue) return ApplyMarkup(basePrice, regionWideMarkup.Value);
            return basePrice;
        }

        private static decimal ResolveFullPrice(decimal basePrice, Guid productId,
            Dictionary<Guid, decimal> customerProductMarkups, decimal? customerWideMarkup,
            Dictionary<Guid, decimal> regionProductMarkups, decimal? regionWideMarkup)
        {
            if (customerProductMarkups.TryGetValue(productId, out var cm)) return ApplyMarkup(basePrice, cm);
            if (customerWideMarkup.HasValue) return ApplyMarkup(basePrice, customerWideMarkup.Value);
            return ResolveRegionPrice(basePrice, productId, regionProductMarkups, regionWideMarkup);
        }
    }
}

public class GetOfflineProductsByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/offline/products",
            async (Guid token, DateTime? since, ISender sender) =>
            {
                var result = await sender.Send(new GetOfflineProductsByDriverToken.Query { Token = token, Since = since });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get product catalogue for offline use (driver portal)")
        .WithDescription("Returns full product catalogue. Pass ?since=ISO8601 to get only records modified after that timestamp.")
        .Produces<GetOfflineProductsByDriverToken.OfflineProductsBundle>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
