namespace prohpharmacy_trekking_app.Services.Traccar;

public class NullTraccarService(ILogger<NullTraccarService> logger) : ITraccarService
{
    public Task<TraccarPosition?> GetCurrentPositionAsync(int traccarDeviceId, CancellationToken ct = default)
    {
        logger.LogWarning("TraccarSettings:BaseUrl is not configured. Returning null position.");
        return Task.FromResult<TraccarPosition?>(null);
    }

    public Task<TraccarDevice?> GetDeviceAsync(int traccarDeviceId, CancellationToken ct = default)
    {
        logger.LogWarning("TraccarSettings:BaseUrl is not configured. Returning null device.");
        return Task.FromResult<TraccarDevice?>(null);
    }

    public Task<List<TraccarPosition>> GetPositionHistoryAsync(int traccarDeviceId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        logger.LogWarning("TraccarSettings:BaseUrl is not configured. Returning empty history.");
        return Task.FromResult(new List<TraccarPosition>());
    }

    public Task<TraccarDevice?> CreateDeviceAsync(string name, string uniqueId, CancellationToken ct = default)
    {
        logger.LogWarning("TraccarSettings:BaseUrl is not configured. Skipping device creation.");
        return Task.FromResult<TraccarDevice?>(null);
    }

    public Task<bool> UpdateDeviceAsync(int traccarDeviceId, string name, CancellationToken ct = default)
    {
        logger.LogWarning("TraccarSettings:BaseUrl is not configured. Skipping device update.");
        return Task.FromResult(false);
    }

    public Task<bool> DeleteDeviceAsync(int traccarDeviceId, CancellationToken ct = default)
    {
        logger.LogWarning("TraccarSettings:BaseUrl is not configured. Skipping device deletion.");
        return Task.FromResult(false);
    }

    public Task<TraccarDriver?> GetDriverAsync(int traccarDriverId, CancellationToken ct = default)
    {
        logger.LogWarning("TraccarSettings:BaseUrl is not configured. Returning null driver.");
        return Task.FromResult<TraccarDriver?>(null);
    }

    public Task<TraccarDriver?> CreateDriverAsync(string name, string uniqueId, Dictionary<string, string>? attributes = null, CancellationToken ct = default)
    {
        logger.LogWarning("TraccarSettings:BaseUrl is not configured. Skipping driver creation.");
        return Task.FromResult<TraccarDriver?>(null);
    }

    public Task<bool> UpdateDriverAsync(int traccarDriverId, string name, Dictionary<string, string>? attributes = null, CancellationToken ct = default)
    {
        logger.LogWarning("TraccarSettings:BaseUrl is not configured. Skipping driver update.");
        return Task.FromResult(false);
    }

    public Task<bool> DeleteDriverAsync(int traccarDriverId, CancellationToken ct = default)
    {
        logger.LogWarning("TraccarSettings:BaseUrl is not configured. Skipping driver deletion.");
        return Task.FromResult(false);
    }
}
