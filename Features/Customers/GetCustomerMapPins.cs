using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class GetCustomerMapPins
{
    public class Query : IRequest<Result<List<CustomerMapPin>>>
    {
        public Guid? BranchId { get; set; }
        public Guid? RegionId { get; set; }
        public Guid? DistrictId { get; set; }
    }

    public class CustomerMapPin
    {
        public Guid LocationId { get; set; }
        public Guid CustomerAccountId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string BusinessName { get; set; } = string.Empty;
        public string? TradingName { get; set; }
        public string CustomerType { get; set; } = string.Empty;
        public string RegistrationStatus { get; set; } = string.Empty;
        public string PrimaryPhoneNumber { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? AccuracyMetres { get; set; }
        public string? StreetAddress { get; set; }
        public string? LandmarkAndDirections { get; set; }
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public Guid RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? PrimaryContactName { get; set; }
        public string? PrimaryContactPhone { get; set; }
        public string? PrimaryContactPortraitUrl { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<List<CustomerMapPin>>>
    {
        public async Task<Result<List<CustomerMapPin>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.CustomerAccounts
                .Include(a => a.Region)
                .Include(a => a.OwningBranch)
                .Include(a => a.People.Where(p => p.IsPrimaryContact && p.IsActive))
                .Include(a => a.Locations.Where(l => l.Latitude != null && l.Longitude != null))
                    .ThenInclude(l => l.Region)
                .Where(a => a.Locations.Any(l => l.Latitude != null && l.Longitude != null))
                .AsNoTracking();

            if (request.BranchId.HasValue)
                query = query.Where(a => a.OwningBranchId == request.BranchId.Value);

            if (request.RegionId.HasValue)
                query = query.Where(a => a.RegionId == request.RegionId.Value
                    || a.Locations.Any(l => l.RegionId == request.RegionId.Value));

            if (request.DistrictId.HasValue)
                query = query.Where(a => a.Locations.Any(l => l.DistrictId == request.DistrictId.Value));

            var accounts = await query.ToListAsync(cancellationToken);

            var pins = accounts.SelectMany(a =>
            {
                var person = a.People.FirstOrDefault();
                return a.Locations.Select(location => new CustomerMapPin
                {
                    LocationId = location.Id,
                    CustomerAccountId = a.Id,
                    CustomerCode = a.CustomerCode,
                    BusinessName = a.BusinessName,
                    TradingName = a.TradingName,
                    CustomerType = a.CustomerType.ToString(),
                    RegistrationStatus = a.RegistrationStatus.ToString(),
                    PrimaryPhoneNumber = a.PrimaryPhoneNumber,
                    IsPrimary = location.IsPrimary,
                    Latitude = (double)location.Latitude!,
                    Longitude = (double)location.Longitude!,
                    AccuracyMetres = location.AccuracyMetres.HasValue ? (double?)location.AccuracyMetres.Value : null,
                    StreetAddress = location.StreetAddress,
                    LandmarkAndDirections = location.LandmarkAndDirections,
                    BranchId = a.OwningBranchId,
                    BranchName = a.OwningBranch.Name,
                    RegionId = location.RegionId,
                    RegionName = location.Region?.Name ?? a.Region.Name,
                    PrimaryContactName = person?.FullName,
                    PrimaryContactPhone = person?.PrimaryPhoneNumber,
                    PrimaryContactPortraitUrl = person?.PortraitUrl
                });
            }).ToList();

            return Result.Success(pins);
        }
    }
}

public class GetCustomerMapPinsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/customers/map-pins", async (
            ISender sender,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? regionId,
            [FromQuery] Guid? districtId) =>
        {
            var result = await sender.Send(new GetCustomerMapPins.Query
            {
                BranchId = branchId,
                RegionId = regionId,
                DistrictId = districtId
            });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Customer map pins")
        .WithDescription(
            "Returns one pin per GPS-recorded location. A customer with multiple locations appears as multiple pins. " +
            "Use locationId + customerAccountId to identify each pin. isPrimary distinguishes the primary location from additional ones. " +
            "Region and district filters match any location, not just the primary.")
        .Produces<List<GetCustomerMapPins.CustomerMapPin>>(200)
        .RequireAuthorization();
    }
}
