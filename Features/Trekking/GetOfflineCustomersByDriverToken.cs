using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetOfflineCustomersByDriverToken
{
    public class Query : IRequest<Result<List<OfflineCustomerItem>>>
    {
        public Guid Token { get; set; }
        public DateTime? Since { get; set; }
    }

    public class OfflineCustomerItem
    {
        public Guid Id { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string BusinessName { get; set; } = string.Empty;
        public string? TradingName { get; set; }
        public string CustomerType { get; set; } = string.Empty;
        public string PrimaryPhoneNumber { get; set; } = string.Empty;
        public string? WhatsAppNumber { get; set; }
        public Guid RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? PrimaryContactName { get; set; }
        public string? PrimaryContactPhone { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? LandmarkAndDirections { get; set; }
        public Guid? ClientGeneratedId { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<List<OfflineCustomerItem>>>
    {
        public async Task<Result<List<OfflineCustomerItem>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);
            if (trip is null)
                return Result.Failure<List<OfflineCustomerItem>>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var query = db.CustomerAccounts
                .Include(c => c.Region)
                .Include(c => c.Locations.Where(l => l.IsPrimary))
                .Include(c => c.People.Where(p => p.IsPrimaryContact && p.IsActive))
                .Where(c => c.RegionId == trip.RegionId)
                .AsNoTracking();

            if (request.Since.HasValue)
                query = query.Where(c => c.CreatedAt >= request.Since.Value || (c.UpdatedAt.HasValue && c.UpdatedAt >= request.Since.Value));

            var customers = await query.ToListAsync(cancellationToken);

            return Result.Success(customers.Select(c =>
            {
                var loc = c.Locations.FirstOrDefault();
                var contact = c.People.FirstOrDefault();
                return new OfflineCustomerItem
                {
                    Id = c.Id,
                    CustomerCode = c.CustomerCode,
                    BusinessName = c.BusinessName,
                    TradingName = c.TradingName,
                    CustomerType = c.CustomerType.ToString(),
                    PrimaryPhoneNumber = c.PrimaryPhoneNumber,
                    WhatsAppNumber = c.WhatsAppNumber,
                    RegionId = c.RegionId,
                    RegionName = c.Region?.Name ?? string.Empty,
                    PrimaryContactName = contact?.FullName,
                    PrimaryContactPhone = contact?.PrimaryPhoneNumber,
                    Latitude = loc?.Latitude,
                    Longitude = loc?.Longitude,
                    LandmarkAndDirections = loc?.LandmarkAndDirections,
                    ClientGeneratedId = c.ClientGeneratedId
                };
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
        .WithDescription("Returns all customers in the trek's region. Pass ?since=ISO8601 for delta sync.")
        .Produces<List<GetOfflineCustomersByDriverToken.OfflineCustomerItem>>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
