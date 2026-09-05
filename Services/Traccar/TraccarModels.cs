using System.Text.Json;
using System.Text.Json.Serialization;

namespace prohpharmacy_trekking_app.Services.Traccar;

public class TraccarPosition
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public bool Valid { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    public double Speed { get; set; }
    public double Course { get; set; }
    public double Accuracy { get; set; }
    public string? Address { get; set; }
    public DateTime DeviceTime { get; set; }
    public DateTime FixTime { get; set; }
    public DateTime ServerTime { get; set; }
    public bool Outdated { get; set; }
    public Dictionary<string, JsonElement> Attributes { get; set; } = [];

    public bool? Ignition => TryGetBool("ignition");
    public bool? Motion => TryGetBool("motion");
    public double? BatteryLevel => TryGetDouble("batteryLevel");

    private bool? TryGetBool(string key) =>
        Attributes.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.True ? true :
        Attributes.TryGetValue(key, out v) && v.ValueKind == JsonValueKind.False ? false : null;

    private double? TryGetDouble(string key) =>
        Attributes.TryGetValue(key, out var v) &&
        (v.ValueKind == JsonValueKind.Number) ? v.GetDouble() : null;
}

public class TraccarDevice
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? LastUpdate { get; set; }
    public int? PositionId { get; set; }
    public bool Disabled { get; set; }
}

public class TraccarDriver
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = [];
}

public class TraccarWebhookPayload
{
    [JsonPropertyName("event")]
    public TraccarWebhookEvent? Event { get; set; }

    [JsonPropertyName("position")]
    public TraccarPosition? Position { get; set; }

    [JsonPropertyName("device")]
    public TraccarDevice? Device { get; set; }
}

public class TraccarWebhookEvent
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public int DeviceId { get; set; }
    public int? PositionId { get; set; }
    public DateTime EventTime { get; set; }
}
