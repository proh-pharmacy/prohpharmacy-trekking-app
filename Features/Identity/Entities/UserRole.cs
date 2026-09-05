namespace prohpharmacy_trekking_app.Features.Identity.Entities;

public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public Guid? AssignedByUserId { get; set; }

    public ApplicationUser User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
