using FuzzySharp;
using Microsoft.Extensions.Options;

namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed class ProductNameMatcher(IOptions<DocumentExtraction.DocumentMatchingOptions> options,
    ProductIdentityGuard identityGuard) : IProductNameMatcher
{
    public ProductNameMatch Match(string name, IReadOnlyList<CatalogueProduct> catalogue)
    {
        var normalized = ProductNameNormalizer.Normalize(name);
        if (normalized.Length == 0)
            return new("Unmatched", null, [], ["No product name was extracted."]);
        var scored = catalogue.Select(p => new
        {
            Product = p,
            Normalized = p.NormalizedName
        }).ToArray();
        var exact = scored.Where(p => p.Normalized == normalized).ToArray();
        if (exact.Length > 0)
            return new(exact.Length == 1 ? "Exact" : "Ambiguous",
                exact.Length == 1 ? exact[0].Product.ProductId : null,
                exact.Take(options.Value.MaxCandidates).Select(p => Candidate(p.Product, 100)).ToList(),
                exact.Length == 1 ? [] : ["Multiple products have the same normalized name."]);

        var ranked = scored.Where(p => !identityGuard.HasConflict(normalized, p.Normalized))
            .Select(p => new
            {
                p.Product, p.Normalized,
                Score = Math.Max(Fuzz.Ratio(normalized, p.Normalized), Fuzz.TokenSortRatio(normalized, p.Normalized))
            })
            .Where(p => p.Score >= options.Value.MinimumSimilarity)
            .OrderByDescending(p => p.Score).ThenBy(p => p.Product.ProductId)
            .Take(Math.Max(2, options.Value.MaxCandidates)).ToArray();
        if (ranked.Length == 0)
            return new("Unmatched", null, [], ["No compatible catalogue product met the matching threshold."]);
        var missing = identityGuard.HasMissingDetails(normalized, ranked[0].Normalized);
        var ambiguous = missing || (ranked.Length > 1 &&
            ranked[0].Score - ranked[1].Score <= options.Value.AmbiguityMargin);
        return new(ambiguous ? "Ambiguous" : "Suggested",
            ambiguous ? null : ranked[0].Product.ProductId,
            ranked.Take(options.Value.MaxCandidates).Select(p => Candidate(p.Product, p.Score)).ToList(),
            [missing ? "Strength, pack size or dosage form is missing; confirm the product." :
                "Confirm the suggested product; similarity is not a probability of correctness."]);
    }

    private static ProductMatchCandidate Candidate(CatalogueProduct p, int score) =>
        new(p.ProductId, p.Name, score, p.BasicUnitId, p.BasicUnitName, p.PackagingUnitId, p.PackagingUnitName);
}
