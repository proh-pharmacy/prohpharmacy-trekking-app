namespace prohpharmacy_trekking_app.Services.Traccar;

public interface ITraccarService
{
    Task<TraccarPosition?> GetCurrentPositionAsync(int traccarDeviceId, CancellationToken ct = default);
    Task<TraccarDevice?> GetDeviceAsync(int traccarDeviceId, CancellationToken ct = default);
    Task<List<TraccarPosition>> GetPositionHistoryAsync(int traccarDeviceId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<TraccarDevice?> CreateDeviceAsync(string name, string uniqueId, CancellationToken ct = default);
    Task<bool> UpdateDeviceAsync(int traccarDeviceId, string name, CancellationToken ct = default);
    Task<bool> DeleteDeviceAsync(int traccarDeviceId, CancellationToken ct = default);

    Task<TraccarDriver?> GetDriverAsync(int traccarDriverId, CancellationToken ct = default);
    Task<TraccarDriver?> CreateDriverAsync(string name, string uniqueId, Dictionary<string, string>? attributes = null, CancellationToken ct = default);
    Task<bool> UpdateDriverAsync(int traccarDriverId, string name, Dictionary<string, string>? attributes = null, CancellationToken ct = default);
    Task<bool> DeleteDriverAsync(int traccarDriverId, CancellationToken ct = default);
}
