namespace prohpharmacy_trekking_app.Features.Identity.Entities;

public class RolePermission
{
    public Guid RoleId { get; set; }
    public string Permission { get; set; } = string.Empty;

    public Role Role { get; set; } = null!;
}
