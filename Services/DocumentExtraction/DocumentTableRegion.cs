namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

// Locate a table by geometry rather than the PDF's internal text-object order.
public static class DocumentTableRegion
{
    public static double? FindHeaderTop(IReadOnlyList<DocumentToken> tokens)
    {
        var names = tokens.Where(t => DocumentColumnNames.Identify(t.Text) == "name");
        var quantities = tokens.Where(t => DocumentColumnNames.Identify(t.Text)
            is "quantity" or "basic" or "packaging").ToList();
        return names.SelectMany(name => quantities
                .Where(quantity => quantity.Right <= name.Left || quantity.Left >= name.Right)
                .Where(quantity => Math.Abs((quantity.Top + quantity.Bottom - name.Top - name.Bottom) / 2)
                    <= Math.Max(quantity.Bottom - quantity.Top, name.Bottom - name.Top))
                .Select(quantity => (double?)Math.Min(name.Top, quantity.Top)))
            .OrderBy(top => top).FirstOrDefault();
    }

    public static IReadOnlyList<DocumentToken> TrimToHeader(IReadOnlyList<DocumentToken> tokens)
    {
        var top = FindHeaderTop(tokens);
        return top.HasValue ? tokens.Where(t => t.Bottom >= top.Value).ToList() : tokens;
    }
}
