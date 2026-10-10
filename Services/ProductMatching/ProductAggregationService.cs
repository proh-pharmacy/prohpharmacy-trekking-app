using prohpharmacy_trekking_app.Features.Products.Matching;

namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed class ProductAggregationService
{
    public List<AggregatedProduct> Aggregate(IEnumerable<MatchedProductRow> rows)
    {
        return rows.Where(r => r.MatchStatus == "Exact" && r.SuggestedProductId.HasValue)
            .GroupBy(r => r.SuggestedProductId!.Value).Select(group =>
            {
                var first = group.First();
                return new AggregatedProduct
                {
                    ProductId = group.Key, ProductName = first.Candidates.FirstOrDefault()?.Name ?? first.ExtractedName,
                    BasicQty = Sum(group.Select(r => r.BasicQty)), PackagingQty = Sum(group.Select(r => r.PackagingQty)),
                    SourceRows = group.Select(r => $"{r.Worksheet ?? "file"}:{r.Row}").ToList(), RequiresReview = true
                };
            }).ToList();
    }

    private static decimal? Sum(IEnumerable<decimal?> values) => values.Any(v => v.HasValue) ? values.Sum(v => v ?? 0) : null;
}
