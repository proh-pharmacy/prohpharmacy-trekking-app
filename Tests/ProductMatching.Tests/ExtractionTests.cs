using Microsoft.Extensions.Options;
using OfficeOpenXml;
using prohpharmacy_trekking_app.Services.DocumentExtraction;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace ProductMatching.Tests;

public sealed class ExtractionTests
{
    private readonly IOptions<DocumentMatchingOptions> options = Options.Create(new DocumentMatchingOptions());

    internal static byte[] Workbook(string product = "Paracetamol 500mg", string quantity = "10")
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Stock");
        sheet.Cells[1, 1].Value = "Product Name";
        sheet.Cells[1, 2].Value = "Quantity";
        sheet.Cells[1, 3].Value = "Unit";
        sheet.Cells[2, 1].Value = product;
        sheet.Cells[2, 2].Value = quantity;
        sheet.Cells[2, 3].Value = "Box";
        return package.GetAsByteArray();
    }

    [Fact]
    public async Task Excel_preserves_source_and_parses_decimal()
    {
        var result = await new ExcelProductExtractor(options).ExtractAsync(Workbook(quantity: "2.5"), ".xlsx", null, default);
        var row = Assert.Single(result.Rows);
        Assert.Equal("Stock", row.Worksheet);
        Assert.Equal(2, row.Row);
        Assert.Equal("2.5", row.QuantityText);
        Assert.Equal(2.5m, row.Quantity);
        Assert.Null(row.PackagingQty);
    }

    [Fact]
    public async Task Excel_missing_mapping_is_an_explicit_failure()
    {
        var error = await Assert.ThrowsAsync<DocumentExtractionException>(() =>
            new ExcelProductExtractor(options).ExtractAsync(Workbook(), ".xlsx",
                new ExcelColumnMapping { ProductNameColumn = "Unknown" }, default));
        Assert.Equal("ColumnNotFound", error.Code);
    }

    [Fact]
    public async Task Excel_enforces_row_limit()
    {
        var limited = Options.Create(new DocumentMatchingOptions { MaxRowsPerFile = 1 });
        using var package = new ExcelPackage(new MemoryStream(Workbook()));
        package.Workbook.Worksheets[0].Cells[3, 1].Value = "Other";
        await Assert.ThrowsAsync<DocumentExtractionException>(() =>
            new ExcelProductExtractor(limited).ExtractAsync(package.GetAsByteArray(), ".xlsx", null, default));
    }

    [Fact]
    public void Content_must_match_extension()
    {
        Assert.Throws<DocumentExtractionException>(() =>
            new DocumentFileValidator(options).Validate("not a PDF"u8.ToArray(), ".pdf"));
        new DocumentFileValidator(options).Validate(Workbook(), ".xlsx");
    }

    [Fact]
    public void Table_parser_keeps_quantity_separate_from_price()
    {
        var result = new DocumentTableRowParser(options).Parse([
            new("Product", 10, 10, 60, 20), new("Qty", 250, 10, 270, 20),
            new("Unit", 320, 10, 350, 20), new("Price", 400, 10, 440, 20),
            new("Paracetamol 500mg", 10, 30, 150, 40), new("10", 250, 30, 265, 40),
            new("Box", 320, 30, 350, 40), new("20.00", 400, 30, 440, 40)
        ], 1, default);
        var row = Assert.Single(result);
        Assert.Equal("Paracetamol 500mg", row.ExtractedName);
        Assert.Equal(10m, row.Quantity);
        Assert.Equal("Box", row.UnitText);
    }

    [Fact]
    public void Table_parser_handles_a_photographed_screen_with_sloped_columns()
    {
        var result = new DocumentTableRowParser(options).Parse([
            new("PRODUCT", 43, 54, 172, 100), new("QUANTITY", 697, 106, 819, 149),
            new("Paracetamol 500mg", 51, 153, 285, 205), new("32 Strips · 32 Box", 691, 198, 922, 247),
            new("Novamol 500mg Supp", 51, 259, 331, 325), new(":20 Tablet", 671, 300, 803, 342),
            new("Actilife Actiwomen", 62, 379, 266, 420), new("151 Piece", 671, 408, 791, 447)
        ], 1, default);

        Assert.Equal(3, result.Count);
        Assert.Equal("Paracetamol 500mg", result[0].ExtractedName);
        Assert.Equal("32 Strips · 32 Box", result[0].QuantityText);
        Assert.Equal("Novamol 500mg Supp", result[1].ExtractedName);
        Assert.Equal("151 Piece", result[2].QuantityText);
    }

    [Fact]
    public async Task Mixed_pdf_uses_ocr_only_on_the_scanned_page()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        page.AddText("Product", 12, new PdfPoint(30, 750), font);
        page.AddText("Quantity", 12, new PdfPoint(300, 750), font);
        page.AddText("Paracetamol 500mg", 12, new PdfPoint(30, 725), font);
        page.AddText("10", 12, new PdfPoint(300, 725), font);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var ocr = new FakeOcr();
        var result = await new PdfProductExtractor(ocr, new DocumentTableRowParser(options), options)
            .ExtractAsync(builder.Build(), ".pdf", null, default);
        Assert.Equal([2], ocr.Pages);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(10m, result.Rows[0].Quantity);
        Assert.Equal(1, result.Rows[0].Page);
        Assert.Equal(2, result.Rows[1].Page);
    }

    [Fact]
    public async Task Ocr_unavailable_is_reported_per_pdf_page()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var client = new PaddleOcrClient(new HttpClient(), options);
        var result = await new PdfProductExtractor(client, new DocumentTableRowParser(options), options)
            .ExtractAsync(builder.Build(), ".pdf", null, default);
        Assert.Empty(result.Rows);
        Assert.Equal("OcrUnavailable", Assert.Single(result.Errors).Code);
    }

    private sealed class FakeOcr : IOcrClient
    {
        public List<int> Pages { get; } = [];
        public Task<IReadOnlyList<DocumentToken>> ReadAsync(byte[] bytes, string extension, int page, CancellationToken cancellationToken)
        {
            Pages.Add(page);
            return Task.FromResult<IReadOnlyList<DocumentToken>>([
                new("Product", 10, 10, 60, 20), new("Quantity", 300, 10, 350, 20),
                new("Amoxicillin 250mg", 10, 30, 150, 40), new("5", 300, 30, 310, 40)
            ]);
        }
    }
}
