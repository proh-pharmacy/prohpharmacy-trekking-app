using prohpharmacy_trekking_app.Features.Customers.Entities;

namespace prohpharmacy_trekking_app.Features.Trekking.Entities;

public class TrekkingTripStop
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrekkingTripId { get; set; }
    public Guid CustomerAccountId { get; set; }
    public int Sequence { get; set; }
    public string? Notes { get; set; }

    public TrekkingTrip TrekkingTrip { get; set; } = null!;
    public CustomerAccount CustomerAccount { get; set; } = null!;
    public ICollection<TrekkingTripStopProduct> Products { get; set; } = [];
}
