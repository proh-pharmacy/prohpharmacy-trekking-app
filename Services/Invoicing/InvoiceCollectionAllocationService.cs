using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;

namespace prohpharmacy_trekking_app.Services.Invoicing;

// Analytical attribution only. No per-product payment records are created.
public sealed class InvoiceCollectionAllocationService(AppDbContext db)
{
    public async Task<Dictionary<Guid, (decimal Collected, decimal Outstanding)>> AllocateAsync(
        IEnumerable<Guid> stopIds, CancellationToken ct)
    {
        var ids = stopIds.Distinct().ToList();
        var invoices = await db.SaleInvoices.Include(i => i.Stop).ThenInclude(s => s.Products)
            .AsNoTracking().Where(i => ids.Contains(i.TrekkingTripStopId)).ToListAsync(ct);
        var result = new Dictionary<Guid, (decimal, decimal)>();
        foreach (var invoice in invoices)
        {
            var lines = invoice.Stop.Products.Where(p => InvoiceAmounts.Total(p) > 0).OrderBy(p => p.Id).ToList();
            var total = lines.Sum(InvoiceAmounts.Total);
            decimal collected = 0, outstanding = 0;
            for (var i = 0; i < lines.Count; i++)
            {
                var share = InvoiceAmounts.Total(lines[i]) / total;
                var paid = i == lines.Count - 1 ? invoice.TotalPaid - collected
                    : decimal.Round(invoice.TotalPaid * share, 2, MidpointRounding.ToZero);
                var balance = i == lines.Count - 1 ? invoice.Balance - outstanding
                    : decimal.Round(invoice.Balance * share, 2, MidpointRounding.ToZero);
                result[lines[i].Id] = (paid, balance);
                collected += paid;
                outstanding += balance;
            }
        }
        return result;
    }
}
