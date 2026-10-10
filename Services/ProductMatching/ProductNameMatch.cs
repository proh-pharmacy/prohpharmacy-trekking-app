namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed record ProductNameMatch(string Status, Guid? SuggestedProductId,
    IReadOnlyList<ProductMatchCandidate> Candidates, IReadOnlyList<string> Warnings);
