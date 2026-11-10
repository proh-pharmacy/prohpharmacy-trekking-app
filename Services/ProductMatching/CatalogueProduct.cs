namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed record CatalogueProduct(Guid ProductId, string Name, Guid BasicUnitId, string BasicUnitName,
    Guid? PackagingUnitId, string? PackagingUnitName)
{
    public string NormalizedName { get; } = ProductNameNormalizer.Normalize(Name);
}
