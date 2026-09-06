using prohpharmacy_trekking_app.Features.Customers.Enums;

namespace prohpharmacy_trekking_app.Features.Customers.Entities;

public class CustomerPerson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerAccountId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public RelationshipType RelationshipType { get; set; }
    public string PrimaryPhoneNumber { get; set; } = string.Empty;
    public string? AlternativePhoneNumber { get; set; }
    public string? EmailAddress { get; set; }
    public string? GhanaCardNumber { get; set; }
    public bool IsPrimaryContact { get; set; }
    public bool IsCreditResponsiblePerson { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public CustomerAccount CustomerAccount { get; set; } = null!;

    public string FullName => MiddleName is not null
        ? $"{FirstName} {MiddleName} {LastName}"
        : $"{FirstName} {LastName}";
}
