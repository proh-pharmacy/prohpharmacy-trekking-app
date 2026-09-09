using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Features.Staff.Entities;

namespace prohpharmacy_trekking_app.Features.Fleet.Entities;

public class TrackingDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? TraccarDeviceId { get; set; }
    public string TraccarUniqueId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public TrackingDeviceStatus Status { get; set; } = TrackingDeviceStatus.Active;
    public Guid? VehicleId { get; set; }
    public Guid? StaffMemberId { get; set; }
    public DateTime? LastReportedAt { get; set; }
    public decimal? LastLatitude { get; set; }
    public decimal? LastLongitude { get; set; }
    public string? LastAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Vehicle? Vehicle { get; set; }
    public StaffMember? StaffMember { get; set; }
}
