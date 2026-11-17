using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class PdfProductExtractor(IOcrClient ocr, DocumentTableRowParser parser,
    IOptions<DocumentMatchingOptions> options) : IProductDocumentExtractor
{
    public bool CanExtract(string extension) => extension == ".pdf";

    public async Task<DocumentExtractionResult> ExtractAsync(byte[] bytes, string extension,
        ExcelColumnMapping? mapping, IReadOnlySet<string>? knownUnits,
        DocumentExtractionProgressCallback? reportProgress, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(bytes);
        if (document.NumberOfPages > options.Value.MaxPages)
            throw new DocumentExtractionException("PageLimit", "The PDF exceeds the page limit.");
        var result = new DocumentExtractionResult();
        var pages = document.GetPages().ToList();
        if (reportProgress is not null)
            await reportProgress(0, document.NumberOfPages, cancellationToken);
        var tokensByPage = new Dictionary<int, IReadOnlyList<DocumentToken>>();
        var ocrPages = new List<int>();
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
            var tokens = words.Select(w => new DocumentToken(w.Text,
                w.BoundingBox.Left, page.Height - w.BoundingBox.Top,
                w.BoundingBox.Right, page.Height - w.BoundingBox.Bottom)).ToList();
            var headerTop = DocumentTableRegion.FindHeaderTop(tokens);
            // A logo above a readable table does not require OCR. Keep whole-page
            // OCR when images may contain table data so mixed content is not lost.
            var needsImageRecognition = page.NumberOfImages > 0 &&
                (!headerTop.HasValue || page.GetImages().Any(image =>
                    page.Height - image.BoundingBox.Bottom >= headerTop.Value));
            if (words.Count == 0 || needsImageRecognition)
                ocrPages.Add(page.Number);
            else
                tokensByPage[page.Number] = tokens;
        }
        if (ocrPages.Count > 0)
        {
            var ocrResults = await ocr.ReadPagesAsync(bytes, ocrPages, cancellationToken);
            foreach (var ocrResult in ocrResults)
            {
                if (ocrResult.Error is null)
                    tokensByPage[ocrResult.Page] = ocrResult.Tokens;
                else
                    result.Errors.Add(new("OcrPageFailed", ocrResult.Error, ocrResult.Page));
            }
        }

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!tokensByPage.TryGetValue(page.Number, out var tokens))
                {
                    if (!result.Errors.Any(error => error.Page == page.Number))
                        result.Errors.Add(new("OcrPageFailed", "The document page could not be processed.", page.Number));
                }
                else
                {
                    tokens = DocumentTableRegion.TrimToHeader(tokens);
                    var rows = parser.Parse(tokens, page.Number, cancellationToken, knownUnits);
                    if (rows.Count == 0)
                        result.Errors.Add(new("NoRows", "No product rows were found on this page.", page.Number));
                    result.Rows.AddRange(rows);
                }
            }
            catch (DocumentExtractionException ex)
            {
                result.Errors.Add(new(ex.Code, ex.Message, page.Number));
            }
            if (result.Rows.Count > options.Value.MaxRowsPerFile)
                throw new DocumentExtractionException("RowLimit", "The PDF exceeds the extracted-row limit.");
            if (reportProgress is not null)
                await reportProgress(page.Number, document.NumberOfPages, cancellationToken);
        }
        return result;
    }
}
