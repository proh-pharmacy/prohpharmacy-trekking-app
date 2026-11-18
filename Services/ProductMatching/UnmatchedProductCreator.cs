using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Products.Entities;
using prohpharmacy_trekking_app.Features.Units.Entities;
using prohpharmacy_trekking_app.Services.DocumentExtraction;

namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed class UnmatchedProductCreator(AppDbContext db)
{
    private readonly Dictionary<string, ProductNameMatch> created = new(StringComparer.Ordinal);

    public ProductNameMatch? FindCreated(string name) =>
        created.GetValueOrDefault(ProductNameNormalizer.Normalize(name));

    public async Task<ProductNameMatch> ResolveAsync(ExtractedProductRow row, ProductNameMatch match,
        bool hasCatalogueCandidates, CancellationToken cancellationToken)
    {
        if (match.Status != "Unmatched" || match.Candidates.Count > 0) return match;
        if (hasCatalogueCandidates)
            return WithWarning(match, "A possible catalogue match exists below the display threshold; automatic creation was skipped.");

        var name = row.ExtractedName.Trim();
        var normalized = ProductNameNormalizer.Normalize(name);
        if (name.Length is < 2 or > 200 || !name.Any(char.IsLetter) ||
            normalized is "product" or "description" or "item" or "quantity" or "qty" or "rate" or "price" or "amount" ||
            DocumentFooterDetector.IsFooter([new DocumentToken(name, 0, 0, 0, 0)]) ||
            row.OcrConfidence is < 0.85)
            return WithWarning(match, "The extracted name needs manual review before a product can be created.");

        // Serialize the final duplicate check and insertion, including competing
        // catalogue writes, without holding a lock during extraction or OCR.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "LOCK TABLE \"Products\", \"Units\" IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
        var names = await db.Products.AsNoTracking().Select(p => new { p.Id, p.Name, p.IsActive })
            .ToListAsync(cancellationToken);
        var existing = names.Where(p => ProductNameNormalizer.Normalize(p.Name) == normalized).ToList();
        if (existing.Count > 0)
        {
            if (existing.Count != 1 || !existing[0].IsActive)
                return WithWarning(match, "An existing or inactive catalogue product has this name; review it instead of creating a duplicate.");
            var product = await db.Products.AsNoTracking().Include(p => p.BasicUnit).Include(p => p.PackagingUnit)
                .SingleAsync(p => p.Id == existing[0].Id, cancellationToken);
            return ToMatch(product, "Exact");
        }
        // Include inactive products and weak trigram candidates in this check.
        if (await db.Products.AnyAsync(p => EF.Functions.TrigramsAreSimilar(p.Name, name), cancellationToken))
            return WithWarning(match, "A possible catalogue match exists; automatic creation was skipped.");

        var units = await db.Units.Where(u => u.IsActive).OrderBy(u => u.Name).ToListAsync(cancellationToken);
        var observed = row.UnitText ?? row.QuantityObservations.Select(o => o.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        var matchingUnits = units.Where(u => ProductUnitMatcher.Is(observed, u.Name)).ToList();
        var unit = matchingUnits.Count == 1 ? matchingUnits[0] :
            units.FirstOrDefault(u => ProductUnitMatcher.Is("Piece", u.Name)) ??
            units.FirstOrDefault(u => ProductUnitMatcher.Is("Unit", u.Name));
        if (unit is null)
        {
            // Reuse an inactive generic unit rather than insert a duplicate name.
            unit = await db.Units.FirstOrDefaultAsync(u => u.Name.ToLower() == "unit", cancellationToken);
            if (unit is null)
            {
                unit = new Unit { Name = "Unit" };
                db.Units.Add(unit);
            }
            else
            {
                unit.IsActive = true;
                unit.UpdatedAt = DateTime.UtcNow;
            }
        }
        var added = new Product
        {
            Name = name, BasicUnitId = unit.Id, BasicUnit = unit,
            // Uploaded prices remain on the result for vehicle stock loading.
            BasicUnitPrice = 0
        };
        db.Products.Add(added);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            db.Entry(added).State = EntityState.Detached;
            db.Entry(unit).State = EntityState.Detached;
            throw;
        }
        var result = ToMatch(added, "AutoCreated");
        created[normalized] = result;
        return result;
    }

    private static ProductNameMatch WithWarning(ProductNameMatch match, string warning) =>
        match with { Warnings = match.Warnings.Append(warning).ToList() };

    private static ProductNameMatch ToMatch(Product product, string status) => new(status, product.Id,
        [new ProductMatchCandidate(product.Id, product.Name, status == "AutoCreated" ? 0 : 100,
            product.BasicUnitId, product.BasicUnit.Name, product.PackagingUnitId, product.PackagingUnit?.Name)],
        status == "AutoCreated" ? ["This product was created from the document; review its name, unit and price."] : []);
}
