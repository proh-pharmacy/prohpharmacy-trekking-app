using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class AddCustomerLocation
{
    public class Command : IRequest<Result<LocationResponse>>
    {
        public Guid CustomerId { get; set; }
        public LocationType LocationType { get; set; } = LocationType.BusinessPremises;
        public Guid RegionId { get; set; }
        public Guid DistrictId { get; set; }
        public string? LandmarkAndDirections { get; set; }
        public string? StreetAddress { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public decimal? AccuracyMetres { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class LocationResponse
    {
        public Guid Id { get; set; }
        public Guid CustomerAccountId { get; set; }
        public string LocationType { get; set; } = string.Empty;
        public Guid RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public Guid DistrictId { get; set; }
        public string DistrictName { get; set; } = string.Empty;
        public string LandmarkAndDirections { get; set; } = string.Empty;
        public string? StreetAddress { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public decimal? AccuracyMetres { get; set; }
        public string CaptureMethod { get; set; } = string.Empty;
        public string VerificationStatus { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.RegionId).NotEmpty();
            RuleFor(x => x.DistrictId).NotEmpty();
            RuleFor(x => x.LandmarkAndDirections).MaximumLength(500).When(x => x.LandmarkAndDirections is not null);
            RuleFor(x => x.StreetAddress).MaximumLength(300).When(x => x.StreetAddress is not null);
            RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue);
            RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue);
            RuleFor(x => x.AccuracyMetres).GreaterThan(0).When(x => x.AccuracyMetres.HasValue);
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth)
        : IRequestHandler<Command, Result<LocationResponse>>
    {
        public async Task<Result<LocationResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<LocationResponse>(Error.ValidationError(validation));

            if (!Guid.TryParse(auth.GetUserId(), out var userId))
                return Result.Failure<LocationResponse>(Error.BadRequest("Invalid user context."));

            var staff = await db.StaffMembers
                .FirstOrDefaultAsync(s => s.ApplicationUser!.Id == userId, cancellationToken);

            if (staff is null)
                return Result.Failure<LocationResponse>(Error.CreateNotFoundError("Staff member not found."));

            var customerExists = await db.CustomerAccounts
                .AnyAsync(a => a.Id == request.CustomerId, cancellationToken);

            if (!customerExists)
                return Result.Failure<LocationResponse>(Error.CreateNotFoundError("Customer not found."));

            var district = await db.Districts
                .Include(d => d.Region)
                .FirstOrDefaultAsync(d => d.Id == request.DistrictId, cancellationToken);

            if (district is null)
                return Result.Failure<LocationResponse>(Error.CreateNotFoundError("District not found."));

            var captureMethod = request.AccuracyMetres > 0
                ? CaptureMethod.PwaGps
                : CaptureMethod.ManualLocationSelection;

            var location = new CustomerLocation
            {
                CustomerAccountId = request.CustomerId,
                LocationType = request.LocationType,
                RegionId = request.RegionId,
                DistrictId = request.DistrictId,
                LandmarkAndDirections = request.LandmarkAndDirections?.Trim(),
                StreetAddress = request.StreetAddress?.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                AccuracyMetres = request.AccuracyMetres,
                CaptureMethod = captureMethod,
                VerificationStatus = captureMethod == CaptureMethod.PwaGps
                    ? LocationVerificationStatus.GpsCaptured
                    : LocationVerificationStatus.Unverified,
                IsPrimary = request.IsPrimary,
                CapturedByStaffId = staff.Id,
                CreatedAt = DateTime.UtcNow
            };

            db.CustomerLocations.Add(location);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new LocationResponse
            {
                Id = location.Id,
                CustomerAccountId = location.CustomerAccountId,
                LocationType = location.LocationType.ToString(),
                RegionId = location.RegionId,
                RegionName = district.Region?.Name ?? string.Empty,
                DistrictId = location.DistrictId,
                DistrictName = district.Name,
                LandmarkAndDirections = location.LandmarkAndDirections,
                StreetAddress = location.StreetAddress,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                AccuracyMetres = location.AccuracyMetres,
                CaptureMethod = location.CaptureMethod.ToString(),
                VerificationStatus = location.VerificationStatus.ToString(),
                IsPrimary = location.IsPrimary,
                CreatedAt = location.CreatedAt
            });
        }
    }
}

public class AddCustomerLocationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/customers/{customerId:guid}/locations",
            async (Guid customerId, AddCustomerLocation.Command command, ISender sender) =>
            {
                command.CustomerId = customerId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Created($"api/v1/customers/{customerId}/locations/{result.Value.Id}", result.Value);
            })
            .WithTags("Customers")
            .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
            .WithSummary("Add a location to a customer")
            .Produces<AddCustomerLocation.LocationResponse>(201)
            .Produces<Error>(404)
            .Produces<Error>(422)
            .RequireAuthorization();
    }
}
