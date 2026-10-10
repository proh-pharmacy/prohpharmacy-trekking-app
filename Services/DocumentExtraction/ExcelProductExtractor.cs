using OfficeOpenXml;
using Microsoft.Extensions.Options;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class ExcelProductExtractor(IOptions<DocumentMatchingOptions> options) : IProductDocumentExtractor
{
    public bool CanExtract(string extension) => extension == ".xlsx";

    public Task<DocumentExtractionResult> ExtractAsync(byte[] bytes, string extension,
        ExcelColumnMapping? mapping, IReadOnlySet<string>? knownUnits, CancellationToken cancellationToken)
    {
        mapping ??= new ExcelColumnMapping();
        using var stream = new MemoryStream(bytes, false);
        using var package = new ExcelPackage(stream);
        var sheets = package.Workbook.Worksheets.Where(s => mapping.Worksheet is null ||
            s.Name.Equals(mapping.Worksheet, StringComparison.OrdinalIgnoreCase)).ToList();
        if (sheets.Count == 0)
            throw new DocumentExtractionException("WorksheetNotFound", "The requested worksheet was not found.");
        if (sheets.Count > options.Value.MaxPages)
            throw new DocumentExtractionException("PageLimit", "The workbook exceeds the worksheet limit.");

        var result = new DocumentExtractionResult();
        foreach (var sheet in sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sheet.Dimension is null) continue;
            if (sheet.Dimension.End.Row - mapping.HeaderRow > options.Value.MaxRowsPerFile ||
                sheet.Dimension.End.Column > 256)
                throw new DocumentExtractionException("RowLimit", "The worksheet exceeds the row or column limit.");
            var columns = ResolveColumns(sheet, mapping, out var headerRow);
            for (var rowNumber = headerRow + 1; rowNumber <= sheet.Dimension.End.Row; rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? Cell(string key) => columns.TryGetValue(key, out var col)
                    ? sheet.Cells[rowNumber, col].Text.Trim() : null;
                var name = Cell("name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.Length > options.Value.MaxTextLength)
                    throw new DocumentExtractionException("TextLimit", "A product name exceeds the text limit.");
                var row = new ExtractedProductRow
                {
                    Worksheet = sheet.Name, Row = rowNumber, ExtractedName = name,
                    QuantityText = Cell("quantity"), UnitText = Cell("unit"),
                    BasicQtyText = Cell("basic"), PackagingQtyText = Cell("packaging")
                };
                QuantityParser.Populate(row);
                result.Rows.Add(row);
                if (result.Rows.Count > options.Value.MaxRowsPerFile)
                    throw new DocumentExtractionException("RowLimit", "The workbook exceeds the extracted-row limit.");
            }
        }
        if (result.Rows.Count == 0)
            result.Errors.Add(new("NoRows", "No product rows were found."));
        return Task.FromResult(result);
    }

    private static Dictionary<string, int> ResolveColumns(ExcelWorksheet sheet, ExcelColumnMapping mapping,
        out int headerRow)
    {
        var explicitNames = new Dictionary<string, string?>
        {
            ["name"] = mapping.ProductNameColumn, ["quantity"] = mapping.QuantityColumn,
            ["unit"] = mapping.UnitColumn, ["basic"] = mapping.BasicQtyColumn,
            ["packaging"] = mapping.PackagingQtyColumn
        };
        headerRow = mapping.HeaderRow;
        if (mapping.ProductNameColumn is null && mapping.QuantityColumn is null &&
            mapping.UnitColumn is null && mapping.BasicQtyColumn is null && mapping.PackagingQtyColumn is null)
        {
            var detected = Enumerable.Range(1, Math.Min(sheet.Dimension.End.Row, 100))
                .Select(row => new
                {
                    Row = row,
                    Cells = Enumerable.Range(1, sheet.Dimension.End.Column)
                        .Select(column => DocumentColumnNames.Identify(sheet.Cells[row, column].Text.Trim()))
                        .ToList()
                })
                .Select(x => new
                {
                    x.Row,
                    Score = (x.Cells.Count(k => k == "name") * 5) +
                            (x.Cells.Count(k => k == "quantity") * 4) +
                            (x.Cells.Count(k => k == "unit" || k == "basic" || k == "packaging") * 2),
                    HasName = x.Cells.Contains("name"),
                    HasQuantity = x.Cells.Any(k => k is "quantity" or "unit" or "basic" or "packaging")
                })
                .Where(x => x.HasName && x.HasQuantity)
                .OrderByDescending(x => x.Score).ThenBy(x => x.Row)
                .FirstOrDefault();
            if (detected is not null)
                headerRow = detected.Row;
        }

        var columns = new Dictionary<string, int>();
        for (var c = 1; c <= sheet.Dimension.End.Column; c++)
        {
            var text = sheet.Cells[headerRow, c].Text.Trim();
            foreach (var pair in explicitNames)
            {
                var matches = pair.Value is not null
                    ? text.Equals(pair.Value.Trim(), StringComparison.OrdinalIgnoreCase)
                    : DocumentColumnNames.Identify(text) == pair.Key;
                if (!matches) continue;
                if (!columns.TryAdd(pair.Key, c))
                    throw new DocumentExtractionException("AmbiguousColumns",
                        $"Worksheet '{sheet.Name}' has multiple matching {pair.Key} columns; provide explicit mappings.");
            }
        }
        if (!columns.ContainsKey("name") ||
            explicitNames.Any(p => p.Value is not null && !columns.ContainsKey(p.Key)))
            throw new DocumentExtractionException("ColumnNotFound",
                $"Worksheet '{sheet.Name}' is missing a product-name or quantity column.");
        if (columns.Values.Distinct().Count() != columns.Count)
            throw new DocumentExtractionException("AmbiguousColumns", "Each mapped field must use a different column.");
        return columns;
    }
}
