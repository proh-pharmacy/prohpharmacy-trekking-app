using prohpharmacy_trekking_app.Features.Products.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;

namespace prohpharmacy_trekking_app.Features.Trekking.Entities;

public class TrekkingTripStopReturn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrekkingTripStopId { get; set; }
    public Guid ProductId { get; set; }
    public decimal BasicQtyReturned { get; set; }
    public decimal? PackagingQtyReturned { get; set; }
    public decimal BasicUnitPrice { get; set; }
    public decimal? PackagingUnitPrice { get; set; }
    public decimal? RefundAmount { get; set; }
    public PaymentMethod? RefundMethod { get; set; }
    public string? Reason { get; set; }
    public Guid? RecordedByStaffId { get; set; }
    public Guid? ClientGeneratedId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? GpsAccuracyMetres { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    public TrekkingTripStop TrekkingTripStop { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public StaffMember? RecordedBy { get; set; }
}
