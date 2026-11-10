using prohpharmacy_trekking_app.Services.DocumentExtraction;

namespace prohpharmacy_trekking_app.Services.ProductMatching;

public static class ProductUnitMatcher
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tab"] = "tablet", ["tabs"] = "tablet", ["tablet"] = "tablet", ["tablets"] = "tablet",
        ["strip"] = "strip", ["strips"] = "strip", ["box"] = "box", ["boxes"] = "box",
        ["bottle"] = "bottle", ["bottles"] = "bottle", ["piece"] = "piece", ["pieces"] = "piece", ["pcs"] = "piece"
    };

    public static bool Is(string? observed, string? catalogue)
    {
        if (string.IsNullOrWhiteSpace(observed) || string.IsNullOrWhiteSpace(catalogue)) return false;
        var a = ProductNameNormalizer.Normalize(observed);
        var b = ProductNameNormalizer.Normalize(catalogue);
        return a == b || (Aliases.TryGetValue(a, out var aa) && Aliases.TryGetValue(b, out var bb) && aa == bb);
    }

    public static (decimal? Basic, decimal? Packaging, List<string> Warnings) Map(
        IReadOnlyList<QuantityObservation> observations, ProductMatchCandidate product)
    {
        decimal? basic = null, packaging = null; var warnings = new List<string>();
        foreach (var observation in observations)
        {
            var isBasic = Is(observation.Unit, product.BasicUnitName);
            var isPackaging = Is(observation.Unit, product.PackagingUnitName);
            if (isBasic == isPackaging)
            {
                if (string.IsNullOrWhiteSpace(observation.Unit))
                {
                    // A numeric-only stock column represents the product's
                    // basic unit by convention.
                    basic = (basic ?? 0) + observation.Quantity;
                    continue;
                }
                warnings.Add($"The quantity unit '{observation.Unit}' does not map uniquely to the matched product units.");
                continue;
            }
            if (isBasic) basic = (basic ?? 0) + observation.Quantity;
            else packaging = (packaging ?? 0) + observation.Quantity;
        }
        return (basic, packaging, warnings);
    }
}
