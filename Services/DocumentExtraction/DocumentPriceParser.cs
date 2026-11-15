using System.Globalization;
using System.Text.RegularExpressions;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

// Rate is an absolute basic-unit selling price. Never infer it from a percentage,
// quantity, line total or a packaging unit, or silently repair OCR digits.
public static class DocumentPriceParser
{
    public static void Populate(ExtractedProductRow row)
    {
        row.BasicUnitPrice = null;
        if (string.IsNullOrWhiteSpace(row.PriceText)) return;
        if (TryParse(row.PriceText, out var price))
        {
            if (price > 0) row.BasicUnitPrice = price;
            return;
        }
        row.Warnings.Add("The basic-unit price could not be read reliably; confirm it before loading stock.");
    }

    public static bool RepresentsNoPrice(string? value) =>
        string.IsNullOrWhiteSpace(value) || TryParse(value, out var price) && price == 0;

    private static bool TryParse(string value, out decimal price)
    {
        price = 0;
        var text = Regex.Replace(value.Trim(), @"^(?:GHS|GH₵|GH¢|₵|¢)\s*", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return Regex.IsMatch(text, @"^(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d{1,2})?$",
                   RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) &&
               decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                   CultureInfo.InvariantCulture, out price) && price <= 999999999999.99m;
    }
}
