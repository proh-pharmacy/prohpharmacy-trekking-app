using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Ledger.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Ledger;

public static class GetLedgerSummary
{
    public class Query : IRequest<Result<LedgerSummaryResponse>>
    {
        public bool? HasBalance { get; set; }
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public Guid? BranchId { get; set; }
        public Guid? RegionId { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
    }

    public class LedgerSummaryResponse
    {
        public decimal TotalOutstanding { get; set; }
        public int CustomersWithBalance { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public List<CustomerBalanceResponse> Customers { get; set; } = [];
    }

    public class CustomerBalanceResponse
    {
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string BusinessName { get; set; } = string.Empty;
        public string? PrimaryPhoneNumber { get; set; }
        public string? RegionName { get; set; }
        public string? BranchName { get; set; }
        public decimal TotalDebits { get; set; }
        public decimal TotalCredits { get; set; }
        public decimal CurrentBalance { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<LedgerSummaryResponse>>
    {
        public async Task<Result<LedgerSummaryResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var baseQuery = db.CustomerAccounts.AsNoTracking();

            if (request.BranchId.HasValue)
                baseQuery = baseQuery.Where(ca => ca.OwningBranchId == request.BranchId.Value);

            if (request.RegionId.HasValue)
                baseQuery = baseQuery.Where(ca => ca.RegionId == request.RegionId.Value);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var search = request.Search.ToLower();
                baseQuery = baseQuery.Where(ca =>
                    ca.BusinessName.ToLower().Contains(search) ||
                    ca.CustomerCode.ToLower().Contains(search));
            }

            var projected = baseQuery.Select(ca => new
            {
                ca.Id,
                ca.CustomerCode,
                ca.BusinessName,
                ca.PrimaryPhoneNumber,
                RegionName = (string?)ca.Region!.Name,
                BranchName = (string?)ca.OwningBranch!.Name,
                TotalDebits = db.CustomerLedgerEntries
                    .Where(e => e.CustomerAccountId == ca.Id && e.EntryType == LedgerEntryType.Debit)
                    .Sum(e => (decimal?)e.Amount) ?? 0,
                TotalCredits = db.CustomerLedgerEntries
                    .Where(e => e.CustomerAccountId == ca.Id && e.EntryType == LedgerEntryType.Credit)
                    .Sum(e => (decimal?)e.Amount) ?? 0,
            });

            var totalOutstanding = await projected
                .Where(x => x.TotalDebits > x.TotalCredits)
                .SumAsync(x => x.TotalDebits - x.TotalCredits, cancellationToken);

            var customersWithBalance = await projected
                .CountAsync(x => x.TotalDebits > x.TotalCredits, cancellationToken);

            if (request.HasBalance == true)
                projected = projected.Where(x => x.TotalDebits > x.TotalCredits);

            var sorted = request.Sort switch
            {
                "balance_asc" => projected.OrderBy(x => x.TotalDebits - x.TotalCredits),
                "name_asc" => projected.OrderBy(x => x.BusinessName),
                "name_desc" => projected.OrderByDescending(x => x.BusinessName),
                _ => projected.OrderByDescending(x => x.TotalDebits - x.TotalCredits)
            };

            var totalCount = await sorted.CountAsync(cancellationToken);
            var page = Math.Max(1, request.PageNumber ?? 1);
            var pageSize = Math.Clamp(request.PageSize ?? 20, 1, 100);
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var items = await sorted
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new CustomerBalanceResponse
                {
                    CustomerId = x.Id,
                    CustomerCode = x.CustomerCode,
                    BusinessName = x.BusinessName,
                    PrimaryPhoneNumber = x.PrimaryPhoneNumber,
                    RegionName = x.RegionName,
                    BranchName = x.BranchName,
                    TotalDebits = x.TotalDebits,
                    TotalCredits = x.TotalCredits,
                    CurrentBalance = x.TotalDebits - x.TotalCredits
                })
                .ToListAsync(cancellationToken);

            return Result.Success(new LedgerSummaryResponse
            {
                TotalOutstanding = totalOutstanding,
                CustomersWithBalance = customersWithBalance,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Customers = items
            });
        }
    }
}

public class GetLedgerSummaryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/ledger/summary", async (
            ISender sender,
            [FromQuery] bool? hasBalance,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? regionId,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize) =>
        {
            var result = await sender.Send(new GetLedgerSummary.Query
            {
                HasBalance = hasBalance,
                Search = search,
                Sort = sort,
                BranchId = branchId,
                RegionId = regionId,
                PageNumber = pageNumber,
                PageSize = pageSize
            });

            return result.IsFailure
                ? Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Ledger")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Ledger)
        .WithSummary("Customer balance summary")
        .WithDescription("Returns all customers with their total debits, credits, and outstanding balance. Filter by hasBalance=true to show only customers who owe money. Sort: balance_desc (default), balance_asc, name_asc, name_desc.")
        .Produces<GetLedgerSummary.LedgerSummaryResponse>(200)
        .RequireAuthorization();
    }
}
