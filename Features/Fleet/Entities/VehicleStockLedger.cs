using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Features.Products.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;

namespace prohpharmacy_trekking_app.Features.Fleet.Entities;

public class VehicleStockLedger
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VehicleId { get; set; }
    public Guid ProductId { get; set; }
    public StockChangeType ChangeType { get; set; }
    public StockChangeSource Source { get; set; }
    public decimal BasicQtyChange { get; set; }
    public decimal PackagingQtyChange { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public Guid? AuthorStaffId { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    public Vehicle Vehicle { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public StaffMember? Author { get; set; }
}
