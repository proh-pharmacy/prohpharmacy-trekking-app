using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using prohpharmacy_trekking_app.Services.ProductMatching;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class PdfProductExtractor(IOcrClient ocr, DocumentTableRowParser parser,
    IOptions<DocumentMatchingOptions> options) : IProductDocumentExtractor
{
    public bool CanExtract(string extension) => extension == ".pdf";

    public async Task<DocumentExtractionResult> ExtractAsync(byte[] bytes, string extension,
        ExcelColumnMapping? mapping, IReadOnlySet<string>? knownUnits, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(bytes);
        if (document.NumberOfPages > options.Value.MaxPages)
            throw new DocumentExtractionException("PageLimit", "The PDF exceeds the page limit.");
        var result = new DocumentExtractionResult();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
                IReadOnlyList<DocumentToken> tokens;
                if (words.Count == 0 || page.NumberOfImages > 0)
                    tokens = await ocr.ReadAsync(bytes, extension, page.Number, cancellationToken);
                else
                    tokens = words.Select(w => new DocumentToken(w.Text,
                        w.BoundingBox.Left, page.Height - w.BoundingBox.Top,
                        w.BoundingBox.Right, page.Height - w.BoundingBox.Bottom)).ToList();
                tokens = KeepTableRegion(tokens);
                var rows = parser.Parse(tokens, page.Number, cancellationToken, knownUnits);
                if (rows.Count == 0)
                    result.Errors.Add(new("NoRows", "No product rows were found on this page.", page.Number));
                result.Rows.AddRange(rows);
            }
            catch (DocumentExtractionException ex)
            {
                result.Errors.Add(new(ex.Code, ex.Message, page.Number));
            }
            if (result.Rows.Count > options.Value.MaxRowsPerFile)
                throw new DocumentExtractionException("RowLimit", "The PDF exceeds the extracted-row limit.");
        }
        return result;
    }

    private static IReadOnlyList<DocumentToken> KeepTableRegion(IReadOnlyList<DocumentToken> tokens)
    {
        var quantityHeader = tokens.Select((token, index) => (Token: token, Index: index))
            .FirstOrDefault(item => DocumentColumnNames.Identify(item.Token.Text) == "quantity");
        if (quantityHeader.Token is null)
            return tokens;
        var quantityIndex = quantityHeader.Index;
        var nameIndex = tokens.Select((t, i) => (Token: t, Index: i))
            .Where(x => x.Index < quantityIndex &&
                DocumentColumnNames.Identify(x.Token.Text) == "name")
            .Select(x => x.Index).LastOrDefault(-1);
        return nameIndex < 0 ? tokens : tokens.Skip(nameIndex).ToList();
    }
}
