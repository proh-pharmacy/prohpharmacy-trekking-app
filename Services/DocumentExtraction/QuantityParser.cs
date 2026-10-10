using System.Globalization;
using System.Text.RegularExpressions;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public static class QuantityParser
{
    private static readonly Regex ObservationPattern = new(@"(?<value>\d+(?:\.\d+)?)\s*(?<unit>[A-Za-z][A-Za-z%]*)?", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static decimal? Parse(string? text, string label, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var value = text.Trim();
        if (!Regex.IsMatch(value, @"^\d+(?:\.\d+)?$", RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)) ||
            !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity))
        {
            warnings.Add($"{label} is invalid or ambiguous; enter a non-negative number using a decimal point.");
            return null;
        }
        return quantity;
    }

    public static void Populate(ExtractedProductRow row)
    {
        row.QuantityObservations = ParseObservations(row.QuantityText);
        row.Quantity = Parse(row.QuantityText, "Quantity", row.Warnings);
        if (row.Quantity is null && row.QuantityObservations.Count > 0)
        {
            row.Warnings.Remove("Quantity is invalid or ambiguous; enter a non-negative number using a decimal point.");
            row.Warnings.Remove("No usable quantity was extracted.");
        }
        row.BasicQty = Parse(row.BasicQtyText, "Basic quantity", row.Warnings);
        row.PackagingQty = Parse(row.PackagingQtyText, "Packaging quantity", row.Warnings);
        if (row.QuantityText is not null && (row.BasicQtyText is not null || row.PackagingQtyText is not null))
            row.Warnings.Add("Generic and explicit quantity columns are both present; confirm which values to use.");
        if (row.Quantity is null && row.BasicQty is null && row.PackagingQty is null && row.QuantityObservations.Count == 0)
            row.Warnings.Add("No usable quantity was extracted.");
    }

    public static List<QuantityObservation> ParseObservations(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return ObservationPattern.Matches(text.Replace('·', ' '))
            .Select(m => decimal.TryParse(m.Groups["value"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                ? new QuantityObservation(value, string.IsNullOrWhiteSpace(m.Groups["unit"].Value) ? null : m.Groups["unit"].Value)
                : null)
            .Where(x => x is not null).Cast<QuantityObservation>().ToList();
    }
}
