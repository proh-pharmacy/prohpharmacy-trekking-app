using Microsoft.AspNetCore.SignalR;

namespace prohpharmacy_trekking_app.Hubs;

public class TrackingHub : Hub
{
    public async Task JoinBranch(string branchId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"branch-{branchId}");

    public async Task LeaveBranch(string branchId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"branch-{branchId}");
}

public class PositionBroadcast
{
    public Guid DeviceId { get; set; }
    public int TraccarDeviceId { get; set; }
    public Guid? StaffMemberId { get; set; }
    public string? StaffName { get; set; }
    public Guid? VehicleId { get; set; }
    public string? VehicleRegistration { get; set; }
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Speed { get; set; }
    public double Course { get; set; }
    public DateTime FixTime { get; set; }
    public bool Valid { get; set; }
    public bool? Ignition { get; set; }
    public bool? Motion { get; set; }
    public double? BatteryLevel { get; set; }
    public string? Address { get; set; }
}
