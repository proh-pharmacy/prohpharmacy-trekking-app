using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Staff.Enums;

namespace prohpharmacy_trekking_app.Features.Staff.Entities;

public class StaffMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.Pending;
    public DateOnly JoinedOn { get; set; }
    public string? ProfilePhotoObjectKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public uint RowVersion { get; set; }

    public Branch Branch { get; set; } = null!;
    public ApplicationUser? ApplicationUser { get; set; }
    public ICollection<StaffInvitation> Invitations { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}";
}
