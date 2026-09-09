using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;

namespace prohpharmacy_trekking_app.Features.Customers.Entities;

public class CustomerLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerAccountId { get; set; }
    public LocationType LocationType { get; set; } = LocationType.BusinessPremises;
    public Guid RegionId { get; set; }
    public Guid DistrictId { get; set; }
    public string? StreetAddress { get; set; }
    public string? LandmarkAndDirections { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? AccuracyMetres { get; set; }
    public CaptureMethod CaptureMethod { get; set; }
    public LocationVerificationStatus VerificationStatus { get; set; } = LocationVerificationStatus.Unverified;
    public bool IsPrimary { get; set; } = true;
    public Guid CapturedByStaffId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CustomerAccount CustomerAccount { get; set; } = null!;
    public Region Region { get; set; } = null!;
    public District District { get; set; } = null!;
    public StaffMember CapturedBy { get; set; } = null!;
}
