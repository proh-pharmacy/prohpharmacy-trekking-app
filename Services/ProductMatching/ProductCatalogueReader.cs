using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;

namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed class ProductCatalogueReader(AppDbContext db) : IProductCatalogueReader
{
    public async Task<IReadOnlyList<CatalogueProduct>> ReadAsync(CancellationToken cancellationToken) =>
        await ProductProjection(db.Products.AsNoTracking().Where(product => product.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogueProduct>> FindCandidatesAsync(string name, int limit,
        CancellationToken cancellationToken)
    {
        var searchName = name.Trim();
        if (searchName.Length == 0 || limit <= 0) return [];

        var products = db.Products.AsNoTracking()
            .Where(product => product.IsActive &&
                EF.Functions.TrigramsAreSimilar(product.Name, searchName))
            .OrderByDescending(product => EF.Functions.TrigramsSimilarity(product.Name, searchName))
            .ThenBy(product => product.Id)
            .Take(limit);
        return await ProductProjection(products).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> ReadKnownUnitsAsync(CancellationToken cancellationToken)
    {
        var basicUnits = db.Products.AsNoTracking().Where(product => product.IsActive)
            .Select(product => product.BasicUnit.Name);
        var packagingUnits = db.Products.AsNoTracking()
            .Where(product => product.IsActive && product.PackagingUnitId != null)
            .Select(product => product.PackagingUnit!.Name);
        return (await basicUnits.Concat(packagingUnits).Distinct().ToListAsync(cancellationToken))
            .Select(unit => unit.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IQueryable<CatalogueProduct> ProductProjection(
        IQueryable<Features.Products.Entities.Product> products) =>
        products.Select(product => new CatalogueProduct(product.Id, product.Name,
            product.BasicUnitId, product.BasicUnit.Name, product.PackagingUnitId,
            product.PackagingUnit == null ? null : product.PackagingUnit.Name));
}
