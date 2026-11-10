namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed record DocumentIssue(string Code, string Message, int? Page = null);
