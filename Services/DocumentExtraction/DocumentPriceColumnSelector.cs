namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

// Column order breaks ties only between recognized unit-price headers.
// An amount, cost or quantity column must never become a selling price by position.
public static class DocumentPriceColumnSelector
{
    public static int? Select(IReadOnlyList<string?> keys, int productColumn)
    {
        var prices = Enumerable.Range(0, keys.Count).Where(i => keys[i] == "price").ToList();
        var following = prices.Where(i => i > productColumn).ToList();
        if (following.Count > 0) return following[0];
        return prices.Count > 0 ? prices[^1] : null;
    }
}
