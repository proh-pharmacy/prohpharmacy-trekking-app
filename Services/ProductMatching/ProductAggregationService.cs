using prohpharmacy_trekking_app.Features.Products.Matching;
using prohpharmacy_trekking_app.Services.DocumentExtraction;

namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed class ProductAggregationService
{
    public List<AggregatedProduct> Aggregate(IEnumerable<MatchedProductRow> rows)
    {
        return rows.Where(r => r.MatchStatus == "Exact" && r.SuggestedProductId.HasValue)
            .GroupBy(r => r.SuggestedProductId!.Value).Select(group =>
            {
                var first = group.First();
                // Conflicting or partly missing rates must never be averaged or
                // selected arbitrarily when merging quantities for one product.
                var prices = group.Select(r => r.BasicUnitPrice).Distinct().ToList();
                var conflictingPrices = prices.Count > 1 || group.Any(r =>
                    !DocumentPriceParser.RepresentsNoPrice(r.PriceText) && !r.BasicUnitPrice.HasValue);
                return new AggregatedProduct
                {
                    BasicUnitPrice = !conflictingPrices ? prices.SingleOrDefault() : null,
                    Warnings = conflictingPrices ? ["Source rows have conflicting, missing or unreadable prices; choose a basic-unit price before loading stock."] : [],
                    ProductId = group.Key, ProductName = first.Candidates.FirstOrDefault()?.Name ?? first.ExtractedName,
                    BasicQty = Sum(group.Select(r => r.BasicQty)), PackagingQty = Sum(group.Select(r => r.PackagingQty)),
                    SourceRows = group.Select(r => $"{r.Worksheet ?? "file"}:{r.Row}").ToList(), RequiresReview = true
                };
            }).ToList();
    }

    private static decimal? Sum(IEnumerable<decimal?> values) => values.Any(v => v.HasValue) ? values.Sum(v => v ?? 0) : null;
}
