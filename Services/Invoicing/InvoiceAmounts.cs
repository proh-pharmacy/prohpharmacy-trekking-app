using prohpharmacy_trekking_app.Features.Trekking.Entities;

namespace prohpharmacy_trekking_app.Services.Invoicing;

public static class InvoiceAmounts
{
    public static bool IsRecorded(TrekkingTripStopProduct line) =>
        line.DeliveredAt.HasValue || line.BasicQtyDelivered.HasValue || line.PackagingQtyDelivered.HasValue;

    public static decimal BasicQuantity(TrekkingTripStopProduct line) =>
        IsRecorded(line) ? line.BasicQtyDelivered ?? 0 : line.PlannedBasicQuantity;

    public static decimal PackagingQuantity(TrekkingTripStopProduct line) =>
        IsRecorded(line) ? line.PackagingQtyDelivered ?? 0 : line.PlannedPackagingQuantity ?? 0;

    public static decimal Total(TrekkingTripStopProduct line) => decimal.Round(
        BasicQuantity(line) * line.BasicUnitPrice + PackagingQuantity(line) * (line.PackagingUnitPrice ?? 0),
        2, MidpointRounding.AwayFromZero);
}
