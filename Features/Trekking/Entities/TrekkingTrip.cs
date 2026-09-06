using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;

namespace prohpharmacy_trekking_app.Features.Trekking.Entities;

public class TrekkingTrip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TrekNumber { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public Guid DriverStaffId { get; set; }
    public Guid VehicleId { get; set; }
    public TrekStatus Status { get; set; } = TrekStatus.Draft;
    public string? Notes { get; set; }
    public Guid? DriverToken { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Branch Branch { get; set; } = null!;
    public StaffMember Driver { get; set; } = null!;
    public Vehicle Vehicle { get; set; } = null!;
    public ICollection<TrekkingTripStop> Stops { get; set; } = [];
}
