using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;

namespace prohpharmacy_trekking_app.Services.Pricing;

public sealed class TrekPriceReader(AppDbContext db)
{
    public async Task<TrekPriceContext> ReadAsync(Guid vehicleId, Guid regionId,
        IEnumerable<Guid> productIds, IEnumerable<Guid> customerIds, CancellationToken ct)
    {
        var products = productIds.Distinct().ToList();
        var customers = customerIds.Distinct().ToList();
        var vehiclePrices = await db.VehicleProductStocks.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && products.Contains(s.ProductId) && s.BasicUnitPrice > 0)
            .ToDictionaryAsync(s => s.ProductId, s => s.BasicUnitPrice!.Value, ct);
        var regional = await db.RegionalMarkupRules.AsNoTracking()
            .Where(r => r.RegionId == regionId && (r.ProductId == null || products.Contains(r.ProductId.Value)))
            .ToListAsync(ct);
        var customer = await db.CustomerMarkupRules.AsNoTracking()
            .Where(r => customers.Contains(r.CustomerAccountId) && (r.ProductId == null || products.Contains(r.ProductId.Value)))
            .ToListAsync(ct);
        return new TrekPriceContext(vehiclePrices,
            regional.Where(r => r.ProductId.HasValue).ToDictionary(r => r.ProductId!.Value, r => r.MarkupPercentage),
            regional.FirstOrDefault(r => !r.ProductId.HasValue)?.MarkupPercentage,
            customer.Where(r => r.ProductId.HasValue).ToDictionary(r => (r.CustomerAccountId, r.ProductId!.Value), r => r.MarkupPercentage),
            customer.Where(r => !r.ProductId.HasValue).ToDictionary(r => r.CustomerAccountId, r => r.MarkupPercentage));
    }
}
