using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Trekking.Entities;

namespace prohpharmacy_trekking_app.Services.Invoicing;

public sealed class InvoiceNumberService(AppDbContext db)
{
    public async Task<string> NextAsync(TrekkingTrip trip, CancellationToken ct)
    {
        var scopeId = trip.BranchId ?? trip.RegionId;
        var scopeType = trip.BranchId.HasValue ? "Branch" : "Region";
        var code = trip.BranchId.HasValue
            ? await db.Branches.Where(b => b.Id == trip.BranchId).Select(b => b.Code).SingleAsync(ct)
            : await db.Regions.Where(r => r.Id == trip.RegionId).Select(r => r.Code).SingleAsync(ct);
        var seq = (await db.Database.SqlQueryRaw<long>(
            """
            INSERT INTO "InvoiceNumberTrackers" ("ScopeId", "ScopeType", "LastSequence")
            VALUES ({0}, {1}, 1)
            ON CONFLICT ("ScopeId", "ScopeType") DO UPDATE
            SET "LastSequence" = "InvoiceNumberTrackers"."LastSequence" + 1
            RETURNING "LastSequence" AS "Value"
            """, scopeId, scopeType).ToListAsync(ct)).Single();
        return $"INV-{(code ?? "GH").ToUpperInvariant()}{seq:D5}";
    }
}
