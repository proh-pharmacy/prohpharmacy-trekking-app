using prohpharmacy_trekking_app.Features.Products.Entities;

namespace prohpharmacy_trekking_app.Features.Trekking.Entities;

public class TrekkingTripStopProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrekkingTripStopId { get; set; }
    public Guid ProductId { get; set; }
    public decimal PlannedQuantity { get; set; }

    public TrekkingTripStop TrekkingTripStop { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
