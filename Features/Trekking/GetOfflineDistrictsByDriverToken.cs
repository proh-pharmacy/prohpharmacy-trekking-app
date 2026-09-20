using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetOfflineDistrictsByDriverToken
{
    public class Query : IRequest<Result<List<DistrictSeed>>>
    {
        public Guid Token { get; set; }
        public DateTime? Since { get; set; }
    }

    public class DistrictSeed
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public Guid RegionId { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<List<DistrictSeed>>>
    {
        public async Task<Result<List<DistrictSeed>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<List<DistrictSeed>>(
                    Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var query = db.Districts
                .AsNoTracking()
                .Where(d => d.RegionId == trip.RegionId && d.IsActive);

            if (request.Since.HasValue)
                query = query.Where(d => d.CreatedAt >= request.Since.Value || (d.UpdatedAt.HasValue && d.UpdatedAt >= request.Since.Value));

            var districts = await query
                .OrderBy(d => d.Name)
                .Select(d => new DistrictSeed
                {
                    Id = d.Id,
                    Name = d.Name,
                    Code = d.Code,
                    RegionId = d.RegionId
                })
                .ToListAsync(cancellationToken);

            return Result.Success(districts);
        }
    }
}

public class GetOfflineDistrictsByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/offline/districts",
            async (Guid token, DateTime? since, ISender sender) =>
            {
                var result = await sender.Send(new GetOfflineDistrictsByDriverToken.Query { Token = token, Since = since });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get active districts in the trek's region for offline use (driver portal)")
        .WithDescription("Returns all active districts scoped to the trek's region. Pass ?since=ISO8601 for delta sync — only districts created or updated after that timestamp are returned.")
        .Produces<List<GetOfflineDistrictsByDriverToken.DistrictSeed>>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
