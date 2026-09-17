using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetOfflineTrekByDriverToken
{
    public class Query : IRequest<Result<TrekResponse>>
    {
        public Guid Token { get; set; }
        public DateTime? Since { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<TrekResponse>>
    {
        public async Task<Result<TrekResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Region)
                .Include(t => t.Branch)
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .Include(t => t.Vehicle)
                .Include(t => t.Stops.OrderBy(s => s.Sequence))
                    .ThenInclude(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.Region)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.Locations.Where(l => l.IsPrimary))
                            .ThenInclude(l => l.District)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.People.Where(p => p.IsPrimaryContact && p.IsActive))
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                            .ThenInclude(p => p.BasicUnit)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                            .ThenInclude(p => p.PackagingUnit)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Returns)
                        .ThenInclude(r => r.Product)
                            .ThenInclude(p => p.BasicUnit)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Returns)
                        .ThenInclude(r => r.Product)
                            .ThenInclude(p => p.PackagingUnit)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var stops = trip.Stops
                .OrderBy(s => s.Sequence)
                .Select(s => GetTrek.Handler.MapStop(s))
                .ToList();

            return Result.Success(CreateTrek.Handler.ToResponse(
                trip,
                trip.Region?.Name ?? string.Empty,
                trip.Branch?.Name,
                trip.Driver?.FullName ?? string.Empty,
                trip.SalesStaff?.FullName,
                trip.Vehicle?.DisplayName ?? string.Empty,
                stops));
        }
    }
}

public class GetOfflineTrekByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/offline/trek",
            async (Guid token, DateTime? since, ISender sender) =>
            {
                var result = await sender.Send(new GetOfflineTrekByDriverToken.Query { Token = token, Since = since });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get full trek data for offline use (driver portal)")
        .WithDescription("Returns the driver's assigned trek with all stops, products and returns. Pass ?since=ISO8601 for delta — client compares timestamps and applies changes locally.")
        .Produces<CreateTrek.TrekResponse>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
