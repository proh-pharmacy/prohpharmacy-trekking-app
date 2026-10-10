namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class DocumentExtractionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
