namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed record OcrPageResult(
    int Page,
    IReadOnlyList<DocumentToken> Tokens,
    string? Error);
