namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class ImageProductExtractor(IOcrClient ocr, DocumentTableRowParser parser) : IProductDocumentExtractor
{
    public bool CanExtract(string extension) => extension is ".png" or ".jpg" or ".jpeg";

    public async Task<DocumentExtractionResult> ExtractAsync(byte[] bytes, string extension,
        ExcelColumnMapping? mapping, IReadOnlySet<string>? knownUnits, CancellationToken cancellationToken)
    {
        var tokens = await ocr.ReadAsync(bytes, extension, 1, cancellationToken);
        var rows = parser.Parse(tokens, 1, cancellationToken, knownUnits);
        return new DocumentExtractionResult
        {
            Rows = rows.ToList(),
            Errors = rows.Count == 0 ? [new("NoRows", "No product rows were found in the image.")] : []
        };
    }
}
