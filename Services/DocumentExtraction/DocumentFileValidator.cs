using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class DocumentFileValidator(IOptions<DocumentMatchingOptions> options)
{
    public void Validate(byte[] bytes, string extension)
    {
        if (bytes.Length == 0 || bytes.Length > options.Value.MaxFileBytes)
            throw new DocumentExtractionException("FileSize", "The file is empty or exceeds the file-size limit.");
        var valid = extension switch
        {
            ".pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),
            ".png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }),
            ".xlsx" => bytes.AsSpan().StartsWith(new byte[] { 80, 75, 3, 4 }),
            _ => throw new DocumentExtractionException("UnsupportedFormat", "Supported formats are .xlsx, .pdf, .jpg, .jpeg and .png.")
        };
        if (!valid)
            throw new DocumentExtractionException("InvalidFile", "The file content does not match its extension.");
        if (extension != ".xlsx") return;
        using var stream = new MemoryStream(bytes, false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 2000 || zip.Entries.Sum(e => e.Length) > 50L * 1024 * 1024 ||
            zip.GetEntry("[Content_Types].xml") is null || zip.GetEntry("xl/workbook.xml") is null)
            throw new DocumentExtractionException("InvalidWorkbook", "The workbook is invalid or exceeds the expanded-size limit.");
    }
}
