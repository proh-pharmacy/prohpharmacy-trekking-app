using System.Net.Http.Json;

namespace prohpharmacy_trekking_app.Services.Traccar;

public class TraccarService(HttpClient http, ILogger<TraccarService> logger) : ITraccarService
{
    public async Task<TraccarPosition?> GetCurrentPositionAsync(int traccarDeviceId, CancellationToken ct = default)
    {
        try
        {
            var positions = await http.GetFromJsonAsync<List<TraccarPosition>>(
                $"api/positions?deviceId={traccarDeviceId}", ct);
            return positions?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to get position for device #{DeviceId}", traccarDeviceId);
            return null;
        }
    }

    public async Task<List<TraccarDevice>> GetAllDevicesAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<TraccarDevice>>("api/devices", ct) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to fetch all devices");
            return [];
        }
    }

    public async Task<TraccarDevice?> GetDeviceAsync(int traccarDeviceId, CancellationToken ct = default)
    {
        try
        {
            var devices = await http.GetFromJsonAsync<List<TraccarDevice>>(
                $"api/devices?id={traccarDeviceId}", ct);
            return devices?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to get device #{DeviceId}", traccarDeviceId);
            return null;
        }
    }

    public async Task<List<TraccarPosition>> GetPositionHistoryAsync(
        int traccarDeviceId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        try
        {
            var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            var toStr = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            return await http.GetFromJsonAsync<List<TraccarPosition>>(
                $"api/positions?deviceId={traccarDeviceId}&from={Uri.EscapeDataString(fromStr)}&to={Uri.EscapeDataString(toStr)}", ct) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to get position history for device #{DeviceId}", traccarDeviceId);
            return [];
        }
    }

    public async Task<TraccarDevice?> CreateDeviceAsync(string name, string uniqueId, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/devices", new { name, uniqueId }, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<TraccarDevice>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to create device '{Name}'", name);
            return null;
        }
    }

    public async Task<bool> UpdateDeviceAsync(int traccarDeviceId, string name, CancellationToken ct = default)
    {
        try
        {
            var existing = await GetDeviceAsync(traccarDeviceId, ct);
            if (existing is null) return false;
            var response = await http.PutAsJsonAsync($"api/devices/{traccarDeviceId}",
                new { id = traccarDeviceId, name, uniqueId = existing.UniqueId }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to update device #{DeviceId}", traccarDeviceId);
            return false;
        }
    }

    public async Task<bool> DeleteDeviceAsync(int traccarDeviceId, CancellationToken ct = default)
    {
        try
        {
            var response = await http.DeleteAsync($"api/devices/{traccarDeviceId}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to delete device #{DeviceId}", traccarDeviceId);
            return false;
        }
    }

    public async Task<List<TraccarDriver>> GetAllDriversAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<TraccarDriver>>("api/drivers", ct) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to fetch all drivers");
            return [];
        }
    }

    public async Task<TraccarDriver?> GetDriverAsync(int traccarDriverId, CancellationToken ct = default)
    {
        try
        {
            var drivers = await http.GetFromJsonAsync<List<TraccarDriver>>(
                $"api/drivers?id={traccarDriverId}", ct);
            return drivers?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to get driver #{DriverId}", traccarDriverId);
            return null;
        }
    }

    public async Task<TraccarDriver?> CreateDriverAsync(string name, string uniqueId, Dictionary<string, string>? attributes = null, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/drivers",
                new { name, uniqueId, attributes = attributes ?? [] }, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<TraccarDriver>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to create driver '{Name}'", name);
            return null;
        }
    }

    public async Task<bool> UpdateDriverAsync(int traccarDriverId, string name, Dictionary<string, string>? attributes = null, CancellationToken ct = default)
    {
        try
        {
            var existing = await GetDriverAsync(traccarDriverId, ct);
            if (existing is null) return false;
            var response = await http.PutAsJsonAsync($"api/drivers/{traccarDriverId}",
                new { id = traccarDriverId, name, uniqueId = existing.UniqueId, attributes = attributes ?? existing.Attributes }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to update driver #{DriverId}", traccarDriverId);
            return false;
        }
    }

    public async Task<bool> DeleteDriverAsync(int traccarDriverId, CancellationToken ct = default)
    {
        try
        {
            var response = await http.DeleteAsync($"api/drivers/{traccarDriverId}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to delete driver #{DriverId}", traccarDriverId);
            return false;
        }
    }

    public async Task<bool> LinkDriverToDeviceAsync(int traccarDeviceId, int traccarDriverId, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/permissions",
                new { deviceId = traccarDeviceId, driverId = traccarDriverId }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to link driver #{DriverId} to device #{DeviceId}",
                traccarDriverId, traccarDeviceId);
            return false;
        }
    }

    public async Task<bool> UnlinkDriverFromDeviceAsync(int traccarDeviceId, int traccarDriverId, CancellationToken ct = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, "api/permissions")
            {
                Content = JsonContent.Create(new { deviceId = traccarDeviceId, driverId = traccarDriverId })
            };
            var response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to unlink driver #{DriverId} from device #{DeviceId}",
                traccarDriverId, traccarDeviceId);
            return false;
        }
    }

    public async Task<List<TraccarUser>> GetAllUsersAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<TraccarUser>>("api/users", ct) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to fetch users");
            return [];
        }
    }

    public async Task<TraccarUser?> CreateUserAsync(string name, string email, string password, bool administrator = false, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/users",
                new { name, email, password, administrator }, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<TraccarUser>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to create user '{Email}'", email);
            return null;
        }
    }

    public async Task<TraccarUser?> UpdateUserAsync(int traccarUserId, string name, string email, string? password, bool administrator, bool disabled, CancellationToken ct = default)
    {
        try
        {
            var body = new { id = traccarUserId, name, email, password, administrator, disabled };
            var response = await http.PutAsJsonAsync($"api/users/{traccarUserId}", body, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<TraccarUser>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to update user #{UserId}", traccarUserId);
            return null;
        }
    }

    public async Task<bool> DeleteUserAsync(int traccarUserId, CancellationToken ct = default)
    {
        try
        {
            var response = await http.DeleteAsync($"api/users/{traccarUserId}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traccar: failed to delete user #{UserId}", traccarUserId);
            return false;
        }
    }
}
