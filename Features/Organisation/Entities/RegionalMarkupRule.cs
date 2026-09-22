using prohpharmacy_trekking_app.Features.Products.Entities;

namespace prohpharmacy_trekking_app.Features.Organisation.Entities;

public class RegionalMarkupRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RegionId { get; set; }
    public Guid? ProductId { get; set; }
    public decimal MarkupPercentage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Region Region { get; set; } = null!;
    public Product? Product { get; set; }
}
