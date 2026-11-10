using Microsoft.Extensions.Options;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class DocumentProcessingGate(IOptions<DocumentMatchingOptions> options) : IDisposable
{
    private readonly SemaphoreSlim gate = new(options.Value.MaxConcurrentRequests);
    public Task EnterAsync(CancellationToken cancellationToken) => gate.WaitAsync(cancellationToken);
    public void Release() => gate.Release();
    public void Dispose() => gate.Dispose();
}
