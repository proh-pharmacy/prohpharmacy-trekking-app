using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;

namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed class ProductCatalogueReader(AppDbContext db) : IProductCatalogueReader
{
    public async Task<IReadOnlyList<CatalogueProduct>> ReadAsync(CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking().Where(p => p.IsActive)
            .Select(p => new CatalogueProduct(p.Id, p.Name, p.BasicUnitId, p.BasicUnit.Name,
                p.PackagingUnitId, p.PackagingUnit == null ? null : p.PackagingUnit.Name))
            .ToListAsync(cancellationToken);
}
