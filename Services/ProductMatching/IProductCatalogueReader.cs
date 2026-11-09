namespace prohpharmacy_trekking_app.Services.ProductMatching;

public interface IProductCatalogueReader
{
    Task<IReadOnlyList<CatalogueProduct>> ReadAsync(CancellationToken cancellationToken);

    async Task<IReadOnlyList<CatalogueProduct>> FindCandidatesAsync(string name, int limit,
        CancellationToken cancellationToken) =>
        await ReadAsync(cancellationToken);

    async Task<IReadOnlySet<string>> ReadKnownUnitsAsync(CancellationToken cancellationToken) =>
        (await ReadAsync(cancellationToken))
        .SelectMany(product => new[] { product.BasicUnitName, product.PackagingUnitName })
        .Where(unit => !string.IsNullOrWhiteSpace(unit)).Select(unit => unit!.Trim())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
