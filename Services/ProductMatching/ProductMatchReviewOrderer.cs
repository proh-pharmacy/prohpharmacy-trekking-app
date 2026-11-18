using prohpharmacy_trekking_app.Features.Products.Matching;

namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed class ProductMatchReviewOrderer
{
    public List<MatchedProductRow> Order(IEnumerable<MatchedProductRow> rows)
    {
        return rows
            .OrderBy(row => row.MatchStatus == "AutoCreated" ? 0 : row.MatchStatus == "Unmatched" ? 1 : 2)
            .ThenBy(GetBestSimilarityScore)
            .ToList();
    }

    private static int GetBestSimilarityScore(MatchedProductRow row)
    {
        return row.Candidates.Count == 0
            ? -1
            : row.Candidates.Max(candidate => candidate.SimilarityScore);
    }
}
