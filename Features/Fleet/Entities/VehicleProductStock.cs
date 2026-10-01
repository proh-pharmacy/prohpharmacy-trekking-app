using prohpharmacy_trekking_app.Features.Products.Entities;

namespace prohpharmacy_trekking_app.Features.Fleet.Entities;

public class VehicleProductStock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VehicleId { get; set; }
    public Guid ProductId { get; set; }
    public decimal BasicQuantityOnHand { get; set; }
    public decimal PackagingQuantityOnHand { get; set; }
    public decimal? LowStockThreshold { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Vehicle Vehicle { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
