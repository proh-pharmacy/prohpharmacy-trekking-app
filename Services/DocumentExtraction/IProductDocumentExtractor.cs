namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public interface IProductDocumentExtractor
{
    bool CanExtract(string extension);
    Task<DocumentExtractionResult> ExtractAsync(byte[] bytes, string extension,
        ExcelColumnMapping? mapping, IReadOnlySet<string>? knownUnits, CancellationToken cancellationToken);
}
