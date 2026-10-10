using System.Text;
using System.Text.RegularExpressions;

namespace prohpharmacy_trekking_app.Services.ProductMatching;

public static class ProductNameNormalizer
{
    public static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim();
        normalized = Regex.Replace(normalized, @"(\d)\s+(mg|mcg|ml|g|iu|%)\b", "$1$2",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        // OCR commonly reads zero as the letter O inside dosage strengths and volumes.
        normalized = Regex.Replace(normalized, @"(?<=\d)o(?=(?:mg|mcg|ml|g|iu)\b)", "0",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        normalized = Regex.Replace(normalized, @"(?<=\d)o(?=\d)", "0",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        normalized = Regex.Replace(normalized, @"[^\p{L}\p{N}./%]+", " ",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return Regex.Replace(normalized, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();
    }
}
