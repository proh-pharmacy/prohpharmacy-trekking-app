using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetRegionTreksByDriverToken
{
    public class Query : IRequest<Result<List<RegionTrekSummary>>>
    {
        public Guid Token { get; set; }
    }

    public class RegionTrekSummary
    {
        public Guid TrekId { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public DateOnly ScheduledDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? DriverName { get; set; }
        public string? SalesStaffName { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public int StopsCount { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<List<RegionTrekSummary>>>
    {
        public async Task<Result<List<RegionTrekSummary>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<List<RegionTrekSummary>>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var activeStatuses = new[] { TrekStatus.Scheduled, TrekStatus.InProgress };

            var treks = await db.TrekkingTrips
                .Include(t => t.Region)
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .Where(t => t.RegionId == trip.RegionId && activeStatuses.Contains(t.Status))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var stopCounts = await db.TrekkingTripStops
                .Where(s => treks.Select(t => t.Id).Contains(s.TrekkingTripId))
                .GroupBy(s => s.TrekkingTripId)
                .Select(g => new { TrekId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var countDict = stopCounts.ToDictionary(x => x.TrekId, x => x.Count);

            var result = treks.Select(t => new RegionTrekSummary
            {
                TrekId = t.Id,
                TrekNumber = t.TrekNumber,
                ScheduledDate = t.ScheduledDate,
                Status = t.Status.ToString(),
                DriverName = t.Driver?.FullName,
                SalesStaffName = t.SalesStaff?.FullName,
                RegionName = t.Region?.Name ?? string.Empty,
                StopsCount = countDict.TryGetValue(t.Id, out var c) ? c : 0
            }).ToList();

            return Result.Success(result);
        }
    }
}

public class GetRegionTreksByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/region/treks",
            async (Guid token, ISender sender) =>
            {
                var result = await sender.Send(new GetRegionTreksByDriverToken.Query { Token = token });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get all active treks in the driver's region (driver portal)")
        .Produces<List<GetRegionTreksByDriverToken.RegionTrekSummary>>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
