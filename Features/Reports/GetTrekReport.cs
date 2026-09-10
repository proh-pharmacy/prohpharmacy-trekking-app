using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Reports;

public static class GetTrekReport
{
    public class Query : IRequest<Result<TrekReportResponse>>
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public Guid? BranchId { get; set; }
        public Guid? DriverId { get; set; }
        public string? Status { get; set; }
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
    }

    public class TrekReportResponse
    {
        public int TotalTreks { get; set; }
        public int Completed { get; set; }
        public int Cancelled { get; set; }
        public int InProgress { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public List<TrekReportItem> Treks { get; set; } = [];
    }

    public class TrekReportItem
    {
        public Guid Id { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public DateOnly ScheduledDate { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int StopsCount { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<TrekReportResponse>>
    {
        public async Task<Result<TrekReportResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.TrekkingTrips
                .Include(t => t.Driver)
                .Include(t => t.Branch)
                .Include(t => t.Stops).ThenInclude(s => s.Products)
                .AsNoTracking();

            if (request.From.HasValue)
                query = query.Where(t => t.ScheduledDate >= request.From.Value);
            if (request.To.HasValue)
                query = query.Where(t => t.ScheduledDate <= request.To.Value);
            if (request.BranchId.HasValue)
                query = query.Where(t => t.BranchId == request.BranchId.Value);
            if (request.DriverId.HasValue)
                query = query.Where(t => t.DriverStaffId == request.DriverId.Value);
            if (!string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<TrekStatus>(request.Status, true, out var parsedStatus))
                query = query.Where(t => t.Status == parsedStatus);

            var trips = await query.ToListAsync(cancellationToken);

            var items = trips.Select(t => new TrekReportItem
            {
                Id = t.Id,
                TrekNumber = t.TrekNumber,
                ScheduledDate = t.ScheduledDate,
                DriverName = $"{t.Driver.FirstName} {t.Driver.LastName}",
                BranchName = t.Branch.Name,
                Status = t.Status.ToString(),
                StopsCount = t.Stops.Count,
                TotalCollected = t.Stops.Sum(s => s.Products.Sum(p => p.AmtPaid ?? 0)),
                TotalOutstanding = t.Stops.Sum(s => s.Products.Sum(p => p.Balance ?? 0))
            }).ToList();

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var s = request.Search.ToLower();
                items = items.Where(i =>
                    i.TrekNumber.ToLower().Contains(s) ||
                    i.DriverName.ToLower().Contains(s) ||
                    i.BranchName.ToLower().Contains(s)).ToList();
            }

            var totalCollected = items.Sum(i => i.TotalCollected);
            var totalOutstanding = items.Sum(i => i.TotalOutstanding);

            items = (request.Sort switch
            {
                "scheduledDate_asc" => items.OrderBy(i => i.ScheduledDate),
                "trekNumber_asc" => items.OrderBy(i => i.TrekNumber),
                "trekNumber_desc" => items.OrderByDescending(i => i.TrekNumber),
                "collected_desc" => items.OrderByDescending(i => i.TotalCollected),
                "collected_asc" => items.OrderBy(i => i.TotalCollected),
                "outstanding_desc" => items.OrderByDescending(i => i.TotalOutstanding),
                "outstanding_asc" => items.OrderBy(i => i.TotalOutstanding),
                _ => items.OrderByDescending(i => i.ScheduledDate)
            }).ToList();

            int pageNumber = Math.Max(1, request.PageNumber ?? 1);
            int pageSize = Math.Clamp(request.PageSize ?? 20, 1, 100);
            int totalCount = items.Count;
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            return Result.Success(new TrekReportResponse
            {
                TotalTreks = totalCount,
                Completed = items.Count(i => i.Status == TrekStatus.Completed.ToString()),
                Cancelled = items.Count(i => i.Status == TrekStatus.Cancelled.ToString()),
                InProgress = items.Count(i => i.Status == TrekStatus.InProgress.ToString()),
                TotalCollected = totalCollected,
                TotalOutstanding = totalOutstanding,
                Page = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Treks = items.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList()
            });
        }
    }
}

public class GetTrekReportEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/reports/treks", async (
            ISender sender,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? driverId,
            [FromQuery] string? status,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize) =>
        {
            var result = await sender.Send(new GetTrekReport.Query
            {
                From = from,
                To = to,
                BranchId = branchId,
                DriverId = driverId,
                Status = status,
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Reports")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Reports)
        .WithSummary("Trek performance report")
        .WithDescription("Paginated list of treks with per-trek collection and outstanding totals. Filter by date range, branch, driver, or status. Sort options: scheduledDate_asc/desc (default: desc), trekNumber_asc/desc, collected_asc/desc, outstanding_asc/desc.")
        .Produces<GetTrekReport.TrekReportResponse>(200)
        .RequireAuthorization();
    }
}
