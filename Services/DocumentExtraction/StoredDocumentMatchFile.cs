namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class StoredDocumentMatchFile
{
    public int FileIndex { get; set; }
    public string StorageName { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Length { get; set; }
}
