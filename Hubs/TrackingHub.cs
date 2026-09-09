using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;

namespace prohpharmacy_trekking_app.Hubs;

public class TrackingHub : Hub
{
    private readonly AppDbContext _db;

    public TrackingHub(AppDbContext db) => _db = db;

    public override async Task OnConnectedAsync()
    {
        var devices = await _db.TrackingDevices
            .Include(d => d.StaffMember)
                .ThenInclude(s => s!.Branch)
            .Include(d => d.Vehicle)
            .Where(d => d.LastLatitude != null && d.LastLongitude != null && d.LastReportedAt != null)
            .AsNoTracking()
            .ToListAsync();

        foreach (var device in devices)
        {
            await Clients.Caller.SendAsync("PositionUpdated", new PositionBroadcast
            {
                DeviceId = device.Id,
                TraccarDeviceId = device.TraccarDeviceId ?? 0,
                StaffMemberId = device.StaffMemberId,
                StaffName = device.StaffMember?.FullName,
                VehicleId = device.VehicleId,
                VehicleRegistration = device.Vehicle?.RegistrationNumber,
                BranchId = device.StaffMember?.BranchId,
                BranchName = device.StaffMember?.Branch?.Name,
                Latitude = (double)device.LastLatitude!,
                Longitude = (double)device.LastLongitude!,
                Speed = 0,
                Course = 0,
                FixTime = device.LastReportedAt!.Value,
                Valid = true,
                Address = device.LastAddress
            });
        }

        await base.OnConnectedAsync();
    }

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
