namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed record ProductMatchCandidate(Guid ProductId, string Name, int SimilarityScore,
    Guid BasicUnitId, string BasicUnitName, Guid? PackagingUnitId, string? PackagingUnitName);
