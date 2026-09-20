using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetOfflineCustomersByDriverToken
{
    public class Query : IRequest<Result<List<CreateCustomer.CustomerResponse>>>
    {
        public Guid Token { get; set; }
        public DateTime? Since { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<List<CreateCustomer.CustomerResponse>>>
    {
        public async Task<Result<List<CreateCustomer.CustomerResponse>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);
            if (trip is null)
                return Result.Failure<List<CreateCustomer.CustomerResponse>>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var query = db.CustomerAccounts
                .Include(c => c.Region)
                .Include(c => c.OwningBranch)
                .Include(c => c.RegisteredBy)
                .Include(c => c.People.Where(p => p.IsPrimaryContact && p.IsActive))
                .Include(c => c.Locations)
                    .ThenInclude(l => l.District)
                .Include(c => c.Locations)
                    .ThenInclude(l => l.Region)
                .Where(c => c.RegionId == trip.RegionId
                    || c.Locations.Any(l => l.RegionId == trip.RegionId))
                .AsNoTracking();

            if (request.Since.HasValue)
                query = query.Where(c =>
                    c.CreatedAt >= request.Since.Value
                    || (c.UpdatedAt.HasValue && c.UpdatedAt >= request.Since.Value)
                    || c.Locations.Any(l => l.CreatedAt >= request.Since.Value));

            var customers = await query.ToListAsync(cancellationToken);

            return Result.Success(customers.Select(c =>
            {
                var primaryLocation = c.Locations.FirstOrDefault(l => l.IsPrimary);
                var additionalLocations = c.Locations.Where(l => !l.IsPrimary).ToList();
                return CreateCustomer.Handler.ToResponse(
                    c, c.Region, c.OwningBranch, c.RegisteredBy,
                    c.People.FirstOrDefault(), primaryLocation, primaryLocation?.District, additionalLocations);
            }).ToList());
        }
    }
}

public class GetOfflineCustomersByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/offline/customers",
            async (Guid token, DateTime? since, ISender sender) =>
            {
                var result = await sender.Send(new GetOfflineCustomersByDriverToken.Query { Token = token, Since = since });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get customers in the driver's region for offline use (driver portal)")
        .WithDescription("Returns all customers in the trek's region as full CustomerResponse objects — same shape as the admin GET /api/v1/customers/{id}. Includes primaryLocation, additionalLocations, primaryPerson, and all location details. Pass ?since=ISO8601 for delta sync.")
        .Produces<List<CreateCustomer.CustomerResponse>>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
