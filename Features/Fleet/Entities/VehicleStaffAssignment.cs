using prohpharmacy_trekking_app.Features.Staff.Entities;

namespace prohpharmacy_trekking_app.Features.Fleet.Entities;

public class VehicleStaffAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VehicleId { get; set; }
    public Guid StaffMemberId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UnassignedAt { get; set; }
    public string? Notes { get; set; }

    public Vehicle Vehicle { get; set; } = null!;
    public StaffMember StaffMember { get; set; } = null!;
}
