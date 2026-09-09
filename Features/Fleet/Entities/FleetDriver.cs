using prohpharmacy_trekking_app.Features.Staff.Entities;

namespace prohpharmacy_trekking_app.Features.Fleet.Entities;

public class FleetDriver
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffMemberId { get; set; }
    public int? TraccarDriverId { get; set; }
    public string? TraccarUniqueId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public StaffMember StaffMember { get; set; } = null!;
}
