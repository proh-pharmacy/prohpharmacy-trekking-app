namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public interface IOcrClient
{
    Task<IReadOnlyList<DocumentToken>> ReadAsync(byte[] bytes, string extension, int page,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<OcrPageResult>> ReadPagesAsync(byte[] bytes, IReadOnlyList<int> pages,
        CancellationToken cancellationToken);
}
