using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;

namespace prohpharmacy_trekking_app.Services.Invoicing;

// Keeps every issued stop invoice mutation (admin, driver and offline) in the
// same transaction. Draft stops remain planning data; changing the trek to
// Scheduled causes every missing stop invoice to be issued atomically.
public sealed class InvoiceSaveCoordinator(AppDbContext db)
{
    public async Task<int> SaveAsync(Func<Task<int>> save, CancellationToken ct)
    {
        db.ChangeTracker.DetectChanges();
        var stopEntries = db.ChangeTracker.Entries<TrekkingTripStop>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var lines = db.ChangeTracker.Entries<TrekkingTripStopProduct>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var payments = db.ChangeTracker.Entries<InvoicePayment>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var trips = db.ChangeTracker.Entries<TrekkingTrip>()
            .Where(e => e.State == EntityState.Deleted ||
                (e.State == EntityState.Modified && e.Property(t => t.Status).IsModified)).ToList();
        if (stopEntries.Count + lines.Count + payments.Count + trips.Count == 0)
            return await save();
        if (payments.Any(e => e.State != EntityState.Added))
            throw new InvoiceRuleException("Recorded payments cannot be changed or deleted.");

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        var stopIds = stopEntries.Select(e => e.Entity.Id)
            .Concat(lines.Select(e => e.Entity.TrekkingTripStopId)).ToHashSet();
        var paymentInvoiceIds = payments.Select(e => e.Entity.SaleInvoiceId).ToList();
        foreach (var id in await db.SaleInvoices.Where(i => paymentInvoiceIds.Contains(i.Id))
                     .Select(i => i.TrekkingTripStopId).ToListAsync(ct)) stopIds.Add(id);
        var changedTripIds = trips.Select(e => e.Entity.Id).ToList();
        foreach (var id in await db.TrekkingTripStops.Where(s => changedTripIds.Contains(s.TrekkingTripId))
                     .Select(s => s.Id).ToListAsync(ct)) stopIds.Add(id);

        // Use the same lock order as payment recording; all invoice updates for
        // a trek serialize, including two simultaneous payment requests.
        var tripIds = (await db.TrekkingTripStops.Where(s => stopIds.Contains(s.Id))
            .Select(s => s.TrekkingTripId).ToListAsync(ct))
            .Concat(stopEntries.Select(e => e.Entity.TrekkingTripId)).Concat(changedTripIds).Distinct().Order().ToList();
        foreach (var id in tripIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TrekkingTrips\" WHERE \"Id\" = {id} FOR UPDATE", ct);
            var tracked = db.TrekkingTrips.Local.FirstOrDefault(t => t.Id == id);
            if (tracked is not null && db.Entry(tracked).State != EntityState.Added)
            {
                var stored = await db.Entry(tracked).GetDatabaseValuesAsync(ct);
                if (stored is null || stored.GetValue<TrekStatus>(nameof(TrekkingTrip.Status)) !=
                    db.Entry(tracked).Property(t => t.Status).OriginalValue)
                    throw new DbUpdateConcurrencyException("The trek status changed while this request was being processed.");
            }
        }

        var deletedTripIds = trips.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Id).ToList();
        if (await db.SaleInvoices.AnyAsync(i => deletedTripIds.Contains(i.TrekkingTripId), ct))
            throw new InvoiceRuleException("A trek with issued invoices cannot be deleted. Cancel an unpaid trek instead.");

        var deletedStopIds = stopEntries.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Id).ToList();
        if (await db.SaleInvoices.AnyAsync(i => deletedStopIds.Contains(i.TrekkingTripStopId), ct))
            throw new InvoiceRuleException("A stop with an issued invoice cannot be deleted.");

        var stops = await db.TrekkingTripStops.Include(s => s.Products).Include(s => s.TrekkingTrip)
            .Where(s => stopIds.Contains(s.Id)).ToListAsync(ct);
        stops.AddRange(stopEntries.Where(e => e.State == EntityState.Added).Select(e => e.Entity));
        // Includes reuse tracked instances. Refresh untouched lines under the lock
        // so a concurrent edit to a different item cannot produce a stale total.
        foreach (var entry in db.ChangeTracker.Entries<TrekkingTripStopProduct>()
                     .Where(e => e.State == EntityState.Unchanged && stopIds.Contains(e.Entity.TrekkingTripStopId)).ToList())
            await entry.ReloadAsync(ct);
        var lifecycle = new InvoiceLifecycleService(db);
        foreach (var stop in stops.DistinctBy(s => s.Id).OrderBy(s => s.Id))
        {
            if (db.Entry(stop).State == EntityState.Deleted) continue;
            stop.TrekkingTrip ??= await db.TrekkingTrips.FindAsync([stop.TrekkingTripId], ct)
                ?? throw new InvoiceRuleException("Trek not found.");
            var changedLines = lines.Where(e => e.Entity.TrekkingTripStopId == stop.Id).ToList();
            var wasClosed = trips.FirstOrDefault(e => e.Entity.Id == stop.TrekkingTripId)?
                .Property(t => t.Status).OriginalValue ?? stop.TrekkingTrip.Status;
            if (changedLines.Count > 0 && wasClosed is TrekStatus.Completed or TrekStatus.Cancelled)
                throw new InvoiceRuleException("Items on a completed or cancelled trek cannot be changed.");
            if (changedLines.Any(e => e.State == EntityState.Deleted && InvoiceAmounts.IsRecorded(e.Entity)))
                throw new InvoiceRuleException("Delivered invoice items cannot be removed. Record a return instead.");
            await lifecycle.SynchronizeAsync(stop, ct);
        }
        var result = await save();
        if (transaction is not null) await transaction.CommitAsync(ct);
        return result;
    }
}
