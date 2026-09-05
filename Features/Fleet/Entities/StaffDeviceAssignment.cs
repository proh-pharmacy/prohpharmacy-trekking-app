using prohpharmacy_trekking_app.Features.Staff.Entities;

namespace prohpharmacy_trekking_app.Features.Fleet.Entities;

public class StaffDeviceAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffMemberId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UnassignedAt { get; set; }
    public string? Notes { get; set; }

    public StaffMember StaffMember { get; set; } = null!;
    public TrackingDevice Device { get; set; } = null!;
}
