using prohpharmacy_trekking_app.Features.Products.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;

namespace prohpharmacy_trekking_app.Features.Trekking.Entities;

public class TrekStockLoad
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrekkingTripId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid ProductId { get; set; }
    public decimal BasicQuantityLoaded { get; set; }
    public decimal PackagingQuantityLoaded { get; set; }
    public Guid LoadedByStaffId { get; set; }
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;

    public TrekkingTrip TrekkingTrip { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public StaffMember LoadedBy { get; set; } = null!;
}
