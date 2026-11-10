using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;

namespace prohpharmacy_trekking_app.Services.Invoicing;

public sealed class InvoiceLifecycleService(AppDbContext db)
{
    public async Task SynchronizeAsync(TrekkingTripStop stop, CancellationToken ct)
    {
        var invoice = await db.SaleInvoices.Include(i => i.Payments)
            .SingleOrDefaultAsync(i => i.TrekkingTripStopId == stop.Id, ct)
            ?? db.SaleInvoices.Local.SingleOrDefault(i => i.TrekkingTripStopId == stop.Id);
        var trip = stop.TrekkingTrip;

        // Draft stops are planning data. Their customer and product list remain
        // freely editable until scheduling issues the official invoice.
        // Existing invoices are still synchronized defensively so data created
        // by an older deployment cannot silently diverge.
        if (invoice is null && trip.Status is TrekStatus.Draft or TrekStatus.Cancelled)
            return;

        if (invoice is null)
        {
            invoice = new SaleInvoice
            {
                Stop = stop,
                TrekkingTripStopId = stop.Id,
                TrekkingTripId = stop.TrekkingTripId,
                CustomerAccountId = stop.CustomerAccountId,
                InvoiceNumber = await new InvoiceNumberService(db).NextAsync(trip, ct),
                CreatedOffline = stop.ClientGeneratedId.HasValue
            };
            db.SaleInvoices.Add(invoice);
        }
        if (invoice.CustomerAccountId != stop.CustomerAccountId)
            throw new InvoiceRuleException("The customer on an issued invoice cannot be replaced.");

        var items = stop.Products.Where(p => db.Entry(p).State is not (EntityState.Deleted or EntityState.Detached)).ToList();
        if (items.Any(p => InvoiceAmounts.BasicQuantity(p) < 0 || InvoiceAmounts.PackagingQuantity(p) < 0 ||
            p.BasicUnitPrice < 0 || p.PackagingUnitPrice < 0))
            throw new InvoiceRuleException("Invoice quantities and prices cannot be negative.");
        if (items.Any(p => InvoiceAmounts.PackagingQuantity(p) > 0 && !p.PackagingUnitPrice.HasValue))
            throw new InvoiceRuleException("A packaging quantity requires a packaging unit price.");
        foreach (var item in items) item.AmountDue = InvoiceAmounts.Total(item);

        // A payment on a completed historical invoice must not rewrite its sale.
        var completing = db.Entry(trip).Property(t => t.Status).IsModified;
        if (trip.Status != TrekStatus.Completed || completing || db.Entry(invoice).State == EntityState.Added)
            invoice.TotalAmount = items.Sum(InvoiceAmounts.Total);

        invoice.TotalPaid = invoice.Payments.Sum(p => p.Amount);
        if (invoice.TotalPaid > invoice.TotalAmount && invoice.Payments.Any(p => db.Entry(p).State == EntityState.Added))
            throw new InvoiceRuleException("Payment exceeds the invoice's outstanding balance.");
        if (invoice.TotalPaid > invoice.TotalAmount && db.Entry(invoice).Property(i => i.TotalAmount).OriginalValue != invoice.TotalAmount)
            throw new InvoiceRuleException("The invoice total cannot be reduced below payments already received.");

        if (trip.Status == TrekStatus.Cancelled)
        {
            if (invoice.TotalPaid > 0 || items.Any(InvoiceAmounts.IsRecorded))
                throw new InvoiceRuleException("A trek with payments or recorded deliveries cannot be cancelled.");
            invoice.Status = SaleInvoiceStatus.Voided;
            invoice.Balance = 0;
        }
        else
        {
            if (invoice.Status == SaleInvoiceStatus.Voided)
                throw new InvoiceRuleException("A voided invoice cannot be reopened or changed.");
            invoice.Balance = invoice.TotalAmount - invoice.TotalPaid;
            invoice.Status = invoice.TotalAmount > 0 && invoice.Balance <= 0 ? SaleInvoiceStatus.Paid
                : invoice.TotalPaid > 0 ? SaleInvoiceStatus.PartiallyPaid : SaleInvoiceStatus.Issued;
        }
        invoice.UpdatedAt = DateTime.UtcNow;
        foreach (var payment in invoice.Payments.Where(p => db.Entry(p).State == EntityState.Added))
        {
            payment.InvoiceTotalAtPayment = invoice.TotalAmount;
            payment.BalanceAfterPayment = invoice.Balance;
        }
        await new InvoiceLedgerService(db).SynchronizeAsync(invoice, ct);
    }
}
