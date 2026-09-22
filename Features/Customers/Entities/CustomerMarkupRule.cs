using prohpharmacy_trekking_app.Features.Products.Entities;

namespace prohpharmacy_trekking_app.Features.Customers.Entities;

public class CustomerMarkupRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerAccountId { get; set; }
    public Guid? ProductId { get; set; }
    public decimal MarkupPercentage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public CustomerAccount CustomerAccount { get; set; } = null!;
    public Product? Product { get; set; }
}
