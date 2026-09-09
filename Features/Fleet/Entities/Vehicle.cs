using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Features.Organisation.Entities;

namespace prohpharmacy_trekking_app.Features.Fleet.Entities;

public class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RegistrationNumber { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Colour { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public VehicleOperationalStatus OperationalStatus { get; set; } = VehicleOperationalStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Branch? Branch { get; set; }
    public ICollection<VehicleStaffAssignment> StaffAssignments { get; set; } = [];
}
