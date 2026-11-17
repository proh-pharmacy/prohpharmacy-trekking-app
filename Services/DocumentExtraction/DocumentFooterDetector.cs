using System.Text.RegularExpressions;
using prohpharmacy_trekking_app.Services.ProductMatching;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public static class DocumentFooterDetector
{
    public static bool IsFooter(IEnumerable<DocumentToken> line)
    {
        var raw = string.Join(" ", line.Select(t => t.Text)).Trim();
        var text = ProductNameNormalizer.Normalize(raw);
        return text.StartsWith("subtotal", StringComparison.Ordinal) ||
            text.StartsWith("tax discount", StringComparison.Ordinal) ||
            text.StartsWith("total", StringComparison.Ordinal) ||
            text.StartsWith("notes", StringComparison.Ordinal) ||
            text.StartsWith("unit prices", StringComparison.Ordinal) ||
            text.StartsWith("amount due", StringComparison.Ordinal) ||
            text.StartsWith("account balance", StringComparison.Ordinal) ||
            text.StartsWith("software by", StringComparison.Ordinal) ||
            text.StartsWith("software support", StringComparison.Ordinal) ||
            Regex.IsMatch(text, @"^showing \d+ (?:of|to) \d+ (?:of \d+ )?items\b",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
            Regex.IsMatch(raw, @"^(?:https?://\S+|\S+/invoice/\S+)(?:\s+\d+\s*/\s*\d+)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
            Regex.IsMatch(raw, @"^(?:page\s+)?\d+\s*(?:/|of)\s*\d+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}
