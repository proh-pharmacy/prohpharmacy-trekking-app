using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;

namespace prohpharmacy_trekking_app.Features.Customers.Entities;

public class CustomerAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerCode { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? TradingName { get; set; }
    public CustomerType CustomerType { get; set; }
    public Guid RegionId { get; set; }
    public string PrimaryPhoneNumber { get; set; } = string.Empty;
    public string? WhatsAppNumber { get; set; }
    public Guid OwningBranchId { get; set; }
    public RegistrationStatus RegistrationStatus { get; set; } = RegistrationStatus.Active;
    public Guid RegisteredByStaffId { get; set; }
    public Guid? RegisteredDuringTrekId { get; set; }
    public Guid? ClientGeneratedId { get; set; }
    public bool CreatedOffline { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Region Region { get; set; } = null!;
    public Branch OwningBranch { get; set; } = null!;
    public StaffMember RegisteredBy { get; set; } = null!;
    public ICollection<CustomerPerson> People { get; set; } = [];
    public ICollection<CustomerLocation> Locations { get; set; } = [];
}
