using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetTrekList
{
    public class Query : IRequest<Result<object>>
    {
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public Guid? BranchId { get; set; }
        public string? Status { get; set; }
        public DateOnly? ScheduledDate { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<object>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = _db.TrekkingTrips
                .Include(t => t.Branch)
                .Include(t => t.Driver)
                .Include(t => t.Vehicle)
                .AsNoTracking();

            if (request.BranchId.HasValue)
                query = query.Where(t => t.BranchId == request.BranchId.Value);

            if (!string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<Enums.TrekStatus>(request.Status, ignoreCase: true, out var parsedStatus))
                query = query.Where(t => t.Status == parsedStatus);

            if (request.ScheduledDate.HasValue)
                query = query.Where(t => t.ScheduledDate == request.ScheduledDate.Value);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var search = request.Search.ToLower();
                query = query.Where(t =>
                    t.TrekNumber.ToLower().Contains(search) ||
                    (t.Driver.FirstName + " " + t.Driver.LastName).ToLower().Contains(search));
            }

            var result = await new QueryBuilder<Entities.TrekkingTrip>(query)
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(t => (object)CreateTrek.Handler.ToResponse(
                    t,
                    t.Branch?.Name ?? string.Empty,
                    t.Driver?.FullName ?? string.Empty,
                    t.Vehicle?.DisplayName ?? string.Empty,
                    []));

            return Result.Success(result);
        }
    }
}

public class GetTrekListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks", async (
            ISender sender,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromQuery] Guid? branchId,
            [FromQuery] string? status,
            [FromQuery] DateOnly? scheduledDate) =>
        {
            var result = await sender.Send(new GetTrekList.Query
            {
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize,
                BranchId = branchId,
                Status = status,
                ScheduledDate = scheduledDate
            });

            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("List / search trekking trips")
        .WithDescription("Filter by branchId, status (Draft|Scheduled|InProgress|Completed|Cancelled), or scheduledDate. Search by trek number or driver name.")
        .Produces<Paginator.PaginatedData<TrekResponse>>(200)
        .RequireAuthorization();
    }
}
