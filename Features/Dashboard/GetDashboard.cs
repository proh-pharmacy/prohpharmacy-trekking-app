using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Ledger.Enums;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Dashboard;

public static class GetDashboard
{
    public class Query : IRequest<Result<DashboardResponse>>
    {
        public string? Period { get; set; }
        public Guid? BranchId { get; set; }
    }

    public class DashboardResponse
    {
        public string Period { get; set; } = string.Empty;
        public decimal TotalCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
        public int ActiveTreks { get; set; }
        public int CustomersWithDebt { get; set; }
        public List<CollectionDataPoint> CollectionsOverTime { get; set; } = [];
        public List<PaymentMethodPoint> ByPaymentMethod { get; set; } = [];
    }

    public class CollectionDataPoint
    {
        public string Label { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public decimal Total { get; set; }
    }

    public class PaymentMethodPoint
    {
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public int Transactions { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<DashboardResponse>>
    {
        public async Task<Result<DashboardResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var period = request.Period?.ToLower() == "month" ? "month" : "week";

            DateOnly periodStart, periodEnd;
            if (period == "month")
            {
                periodStart = new DateOnly(today.Year, today.Month, 1);
                periodEnd = periodStart.AddMonths(1).AddDays(-1);
            }
            else
            {
                int daysFromMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
                periodStart = today.AddDays(-daysFromMonday);
                periodEnd = periodStart.AddDays(6);
            }

            var fromUtc = DateTime.SpecifyKind(periodStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var toUtc = DateTime.SpecifyKind(periodEnd.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

            // ── Credit entries for the period ─────────────────────────────────────
            var creditQuery = db.CustomerLedgerEntries
                .Where(e => e.EntryType == LedgerEntryType.Credit
                         && e.RecordedAt >= fromUtc
                         && e.RecordedAt <= toUtc)
                .AsNoTracking();
            if (request.BranchId.HasValue)
                creditQuery = creditQuery.Where(e => e.CustomerAccount.OwningBranchId == request.BranchId.Value);

            var creditEntries = await creditQuery
                .Select(e => new
                {
                    e.Amount,
                    e.RecordedAt,
                    PaymentMethod = e.PaymentMethod ?? "Unspecified"
                })
                .ToListAsync(cancellationToken);

            // ── All-time balances per customer ────────────────────────────────────
            var balanceQuery = db.CustomerAccounts.AsNoTracking();
            if (request.BranchId.HasValue)
                balanceQuery = balanceQuery.Where(ca => ca.OwningBranchId == request.BranchId.Value);

            var balances = await balanceQuery
                .Select(ca => new
                {
                    TotalDebits = db.CustomerLedgerEntries
                        .Where(e => e.CustomerAccountId == ca.Id && e.EntryType == LedgerEntryType.Debit)
                        .Sum(e => (decimal?)e.Amount) ?? 0,
                    TotalCredits = db.CustomerLedgerEntries
                        .Where(e => e.CustomerAccountId == ca.Id && e.EntryType == LedgerEntryType.Credit)
                        .Sum(e => (decimal?)e.Amount) ?? 0
                })
                .ToListAsync(cancellationToken);

            // ── Active treks ──────────────────────────────────────────────────────
            var trekQuery = db.TrekkingTrips
                .Where(t => t.Status == TrekStatus.InProgress || t.Status == TrekStatus.Scheduled)
                .AsNoTracking();
            if (request.BranchId.HasValue)
                trekQuery = trekQuery.Where(t => t.BranchId == request.BranchId.Value);
            var activeTreks = await trekQuery.CountAsync(cancellationToken);

            // ── Aggregates ────────────────────────────────────────────────────────
            var totalCollected = creditEntries.Sum(e => e.Amount);
            var customerBalances = balances.Select(b => b.TotalDebits - b.TotalCredits).ToList();
            var totalOutstanding = customerBalances.Where(b => b > 0).Sum();
            var customersWithDebt = customerBalances.Count(b => b > 0);

            // ── Collections over time (zero-filled) ───────────────────────────────
            var byDay = creditEntries
                .GroupBy(e => DateOnly.FromDateTime(e.RecordedAt))
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

            var allDays = Enumerable
                .Range(0, periodEnd.DayNumber - periodStart.DayNumber + 1)
                .Select(d => periodStart.AddDays(d));

            var collectionsOverTime = allDays.Select(day => new CollectionDataPoint
            {
                Label = period == "week"
                    ? day.DayOfWeek.ToString()[..3]
                    : day.Day.ToString(),
                Date = day,
                Total = byDay.TryGetValue(day, out var amt) ? amt : 0
            }).ToList();

            // ── By payment method ─────────────────────────────────────────────────
            var byPaymentMethod = creditEntries
                .GroupBy(e => e.PaymentMethod)
                .Select(g => new PaymentMethodPoint
                {
                    PaymentMethod = g.Key,
                    Total = g.Sum(e => e.Amount),
                    Transactions = g.Count()
                })
                .OrderByDescending(b => b.Total)
                .ToList();

            return Result.Success(new DashboardResponse
            {
                Period = period,
                TotalCollected = totalCollected,
                TotalOutstanding = totalOutstanding,
                ActiveTreks = activeTreks,
                CustomersWithDebt = customersWithDebt,
                CollectionsOverTime = collectionsOverTime,
                ByPaymentMethod = byPaymentMethod
            });
        }
    }
}

public class GetDashboardEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/dashboard", async (
            ISender sender,
            [FromQuery] string? period,
            [FromQuery] Guid? branchId) =>
        {
            var result = await sender.Send(new GetDashboard.Query
            {
                Period = period,
                BranchId = branchId
            });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Dashboard")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Dashboard)
        .WithSummary("Dashboard summary")
        .WithDescription(
            "Returns KPI cards, a time-series collections chart, and a collections-by-payment-method breakdown. " +
            "`period=week` (default) groups by day across the current calendar week (Mon–Sun). " +
            "`period=month` groups by day across the current calendar month. " +
            "KPIs 1 (totalCollected) and the chart data are scoped to the selected period. " +
            "totalOutstanding, activeTriks, and customersWithDebt are always current state (all-time). " +
            "Optionally filter everything by branchId.")
        .Produces<GetDashboard.DashboardResponse>(200)
        .RequireAuthorization();
    }
}
