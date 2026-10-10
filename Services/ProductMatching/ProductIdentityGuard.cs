using System.Text.RegularExpressions;

namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed class ProductIdentityGuard
{
    private static readonly string[][] Forms =
    [
        ["tablet", "tablets", "tab", "tabs"],
        ["capsule", "capsules", "cap", "caps"],
        ["syrup", "syr"],
        ["suspension", "susp"],
        ["cream"], ["ointment"], ["injection", "inj"],
        ["suppository", "suppositories", "supp"], ["drops"], ["solution"]
    ];

    public bool HasConflict(string source, string candidate)
    {
        var sourceNumbers = Numbers(source);
        var candidateNumbers = Numbers(candidate);
        if (sourceNumbers.Count > 0 && candidateNumbers.Count > 0 &&
            !sourceNumbers.SetEquals(candidateNumbers))
            return true;
        var sourceForm = Form(source);
        var candidateForm = Form(candidate);
        return sourceForm is not null && candidateForm is not null && sourceForm != candidateForm;
    }

    public bool HasMissingDetails(string source, string candidate) =>
        Numbers(source).Count != Numbers(candidate).Count ||
        (Form(source) is null) != (Form(candidate) is null);

    private static HashSet<string> Numbers(string value) =>
        Regex.Matches(value, @"\d+(?:\.\d+)?(?:\s*(?:mg|mcg|ml|g|iu|%))?",
                RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))
            .Select(m => m.Value.Replace(" ", "").ToLowerInvariant()).ToHashSet();

    private static string? Form(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return Forms.FirstOrDefault(group => group.Any(words.Contains))?.First();
    }
}
