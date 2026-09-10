using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Ledger.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Reports;

public static class GetCollectionsReport
{
    public class Query : IRequest<Result<CollectionsReportResponse>>
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public Guid? BranchId { get; set; }
        public Guid? RegionId { get; set; }
    }

    public class CollectionsReportResponse
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public decimal TotalCollected { get; set; }
        public int TotalTransactions { get; set; }
        public List<PaymentMethodBreakdown> ByPaymentMethod { get; set; } = [];
        public List<BranchBreakdown> ByBranch { get; set; } = [];
    }

    public class PaymentMethodBreakdown
    {
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public int Transactions { get; set; }
    }

    public class BranchBreakdown
    {
        public string BranchName { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public int Transactions { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<CollectionsReportResponse>>
    {
        public async Task<Result<CollectionsReportResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var fromUtc = request.From.HasValue
                ? DateTime.SpecifyKind(request.From.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                : (DateTime?)null;
            var toUtc = request.To.HasValue
                ? DateTime.SpecifyKind(request.To.Value.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc)
                : (DateTime?)null;

            var query = db.CustomerLedgerEntries
                .Where(e => e.EntryType == LedgerEntryType.Credit)
                .AsNoTracking();

            if (fromUtc.HasValue)
                query = query.Where(e => e.RecordedAt >= fromUtc.Value);
            if (toUtc.HasValue)
                query = query.Where(e => e.RecordedAt <= toUtc.Value);
            if (request.BranchId.HasValue)
                query = query.Where(e => e.CustomerAccount.OwningBranchId == request.BranchId.Value);
            if (request.RegionId.HasValue)
                query = query.Where(e => e.CustomerAccount.RegionId == request.RegionId.Value);

            var rows = await query
                .Select(e => new
                {
                    e.Amount,
                    PaymentMethod = e.PaymentMethod ?? "Unspecified",
                    BranchName = e.CustomerAccount.OwningBranch!.Name
                })
                .ToListAsync(cancellationToken);

            var byPaymentMethod = rows
                .GroupBy(r => r.PaymentMethod)
                .Select(g => new PaymentMethodBreakdown
                {
                    PaymentMethod = g.Key,
                    Total = g.Sum(r => r.Amount),
                    Transactions = g.Count()
                })
                .OrderByDescending(b => b.Total)
                .ToList();

            var byBranch = rows
                .GroupBy(r => r.BranchName)
                .Select(g => new BranchBreakdown
                {
                    BranchName = g.Key,
                    Total = g.Sum(r => r.Amount),
                    Transactions = g.Count()
                })
                .OrderByDescending(b => b.Total)
                .ToList();

            return Result.Success(new CollectionsReportResponse
            {
                From = request.From,
                To = request.To,
                TotalCollected = rows.Sum(r => r.Amount),
                TotalTransactions = rows.Count,
                ByPaymentMethod = byPaymentMethod,
                ByBranch = byBranch
            });
        }
    }
}

public class GetCollectionsReportEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/reports/collections", async (
            ISender sender,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? regionId) =>
        {
            var result = await sender.Send(new GetCollectionsReport.Query
            {
                From = from,
                To = to,
                BranchId = branchId,
                RegionId = regionId
            });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Reports")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Reports)
        .WithSummary("Collections report")
        .WithDescription("Returns total credit collections broken down by payment method and by branch. Filter by date range (filters by RecordedAt), branchId, or regionId.")
        .Produces<GetCollectionsReport.CollectionsReportResponse>(200)
        .RequireAuthorization();
    }
}
