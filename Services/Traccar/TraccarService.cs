using System.Net.Http.Json;

namespace prohpharmacy_trekking_app.Services.Traccar;

public class TraccarService(HttpClient http) : ITraccarService
{
    public async Task<TraccarPosition?> GetCurrentPositionAsync(int traccarDeviceId, CancellationToken ct = default)
    {
        var positions = await http.GetFromJsonAsync<List<TraccarPosition>>(
            $"api/positions?deviceId={traccarDeviceId}", ct);
        return positions?.FirstOrDefault();
    }

    public async Task<TraccarDevice?> GetDeviceAsync(int traccarDeviceId, CancellationToken ct = default)
    {
        var devices = await http.GetFromJsonAsync<List<TraccarDevice>>(
            $"api/devices?id={traccarDeviceId}", ct);
        return devices?.FirstOrDefault();
    }

    public async Task<List<TraccarPosition>> GetPositionHistoryAsync(
        int traccarDeviceId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var toStr = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        return await http.GetFromJsonAsync<List<TraccarPosition>>(
            $"api/positions?deviceId={traccarDeviceId}&from={Uri.EscapeDataString(fromStr)}&to={Uri.EscapeDataString(toStr)}", ct) ?? [];
    }

    public async Task<List<TraccarDevice>> GetAllDevicesAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<TraccarDevice>>("api/devices", ct) ?? [];

    public async Task<TraccarDevice?> CreateDeviceAsync(string name, string uniqueId, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/devices", new { name, uniqueId }, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TraccarDevice>(cancellationToken: ct);
    }

    public async Task<bool> UpdateDeviceAsync(int traccarDeviceId, string name, CancellationToken ct = default)
    {
        var existing = await GetDeviceAsync(traccarDeviceId, ct);
        if (existing is null) return false;
        var response = await http.PutAsJsonAsync($"api/devices/{traccarDeviceId}",
            new { id = traccarDeviceId, name, uniqueId = existing.UniqueId }, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteDeviceAsync(int traccarDeviceId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"api/devices/{traccarDeviceId}", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<TraccarDriver?> GetDriverAsync(int traccarDriverId, CancellationToken ct = default)
    {
        var drivers = await http.GetFromJsonAsync<List<TraccarDriver>>(
            $"api/drivers?id={traccarDriverId}", ct);
        return drivers?.FirstOrDefault();
    }

    public async Task<List<TraccarDriver>> GetAllDriversAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<TraccarDriver>>("api/drivers", ct) ?? [];

    public async Task<TraccarDriver?> CreateDriverAsync(string name, string uniqueId, Dictionary<string, string>? attributes = null, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/drivers",
            new { name, uniqueId, attributes = attributes ?? [] }, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TraccarDriver>(cancellationToken: ct);
    }

    public async Task<bool> UpdateDriverAsync(int traccarDriverId, string name, Dictionary<string, string>? attributes = null, CancellationToken ct = default)
    {
        var existing = await GetDriverAsync(traccarDriverId, ct);
        if (existing is null) return false;
        var response = await http.PutAsJsonAsync($"api/drivers/{traccarDriverId}",
            new { id = traccarDriverId, name, uniqueId = existing.UniqueId, attributes = attributes ?? existing.Attributes }, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteDriverAsync(int traccarDriverId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"api/drivers/{traccarDriverId}", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> LinkDriverToDeviceAsync(int traccarDeviceId, int traccarDriverId, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/permissions",
            new { deviceId = traccarDeviceId, driverId = traccarDriverId }, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UnlinkDriverFromDeviceAsync(int traccarDeviceId, int traccarDriverId, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "api/permissions")
        {
            Content = JsonContent.Create(new { deviceId = traccarDeviceId, driverId = traccarDriverId })
        };
        var response = await http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }
}
