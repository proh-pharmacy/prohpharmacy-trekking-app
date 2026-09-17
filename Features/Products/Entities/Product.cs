using prohpharmacy_trekking_app.Features.Units.Entities;

namespace prohpharmacy_trekking_app.Features.Products.Entities;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid BasicUnitId { get; set; }
    public decimal BasicUnitPrice { get; set; }
    public Guid? PackagingUnitId { get; set; }
    public decimal? PackagingUnitPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Unit BasicUnit { get; set; } = null!;
    public Unit? PackagingUnit { get; set; }
}
