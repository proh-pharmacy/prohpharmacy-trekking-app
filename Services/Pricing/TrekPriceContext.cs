using prohpharmacy_trekking_app.Features.Products.Entities;

namespace prohpharmacy_trekking_app.Services.Pricing;

// A request-scoped snapshot of pricing rules; no database queries during resolution.
public sealed class TrekPriceContext(
    IReadOnlyDictionary<Guid, decimal> vehiclePrices,
    IReadOnlyDictionary<Guid, decimal> regionalProducts,
    decimal? regionalDefault,
    IReadOnlyDictionary<(Guid Customer, Guid Product), decimal> customerProducts,
    IReadOnlyDictionary<Guid, decimal> customerDefaults)
{
    public bool HasVehiclePrice(Guid productId) => vehiclePrices.ContainsKey(productId);
    public decimal? VehiclePrice(Guid productId) => vehiclePrices.TryGetValue(productId, out var price) ? price : null;

    public decimal Basic(Product product, Guid? customerId = null) =>
        VehiclePrice(product.Id) ?? ApplyMarkup(product.BasicUnitPrice, product.Id, customerId);

    public decimal? Packaging(Product product, Guid? customerId = null) =>
        product.PackagingUnitId.HasValue && product.PackagingUnitPrice.HasValue
            ? ApplyMarkup(product.PackagingUnitPrice.Value, product.Id, customerId) : null;

    private decimal ApplyMarkup(decimal price, Guid productId, Guid? customerId)
    {
        decimal? percentage = null;
        if (customerId.HasValue)
        {
            if (customerProducts.TryGetValue((customerId.Value, productId), out var specific)) percentage = specific;
            else if (customerDefaults.TryGetValue(customerId.Value, out var general)) percentage = general;
        }
        percentage ??= regionalProducts.TryGetValue(productId, out var regional) ? regional : regionalDefault;
        return percentage.HasValue ? Math.Round(price * (1 + percentage.Value / 100m), 2) : price;
    }
}
