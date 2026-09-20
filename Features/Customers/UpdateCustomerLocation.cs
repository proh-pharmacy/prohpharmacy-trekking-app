using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Customers.AddCustomerLocation;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class UpdateCustomerLocation
{
    public class Command : IRequest<Result<LocationResponse>>
    {
        public Guid CustomerId { get; set; }
        public Guid LocationId { get; set; }
        public LocationType? LocationType { get; set; }
        public Guid? DistrictId { get; set; }
        public string? StreetAddress { get; set; }
        public string? LandmarkAndDirections { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public decimal? AccuracyMetres { get; set; }
        public bool? IsPrimary { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.StreetAddress).MaximumLength(300).When(x => x.StreetAddress is not null);
            RuleFor(x => x.LandmarkAndDirections).MaximumLength(500).When(x => x.LandmarkAndDirections is not null);
            RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue);
            RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue);
            RuleFor(x => x.AccuracyMetres).GreaterThan(0).When(x => x.AccuracyMetres.HasValue);
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator)
        : IRequestHandler<Command, Result<LocationResponse>>
    {
        public async Task<Result<LocationResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<LocationResponse>(Error.ValidationError(validation));

            var location = await db.CustomerLocations
                .FirstOrDefaultAsync(l => l.Id == request.LocationId && l.CustomerAccountId == request.CustomerId, cancellationToken);

            if (location is null)
                return Result.Failure<LocationResponse>(Error.CreateNotFoundError("Location not found for this customer."));

            if (request.LocationType.HasValue)
                location.LocationType = request.LocationType.Value;

            if (request.DistrictId.HasValue)
                location.DistrictId = request.DistrictId.Value;

            if (request.StreetAddress is not null)
                location.StreetAddress = request.StreetAddress.Trim();

            if (request.LandmarkAndDirections is not null)
                location.LandmarkAndDirections = request.LandmarkAndDirections.Trim();

            if (request.Latitude.HasValue)
            {
                location.Latitude = request.Latitude;
                location.Longitude = request.Longitude;
                location.AccuracyMetres = request.AccuracyMetres;
                location.CaptureMethod = CaptureMethod.PwaGps;
                location.VerificationStatus = LocationVerificationStatus.GpsCaptured;
            }

            if (request.IsPrimary == true)
            {
                await db.CustomerLocations
                    .Where(l => l.CustomerAccountId == request.CustomerId && l.IsPrimary && l.Id != request.LocationId)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsPrimary, false), cancellationToken);

                location.IsPrimary = true;
            }
            else if (request.IsPrimary == false)
            {
                location.IsPrimary = false;
            }

            var account = await db.CustomerAccounts.FindAsync([request.CustomerId], cancellationToken);
            if (account is not null)
                account.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            var district = location.DistrictId.HasValue
                ? await db.Districts.Include(d => d.Region).AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == location.DistrictId, cancellationToken)
                : null;

            return Result.Success(new LocationResponse
            {
                Id = location.Id,
                CustomerAccountId = location.CustomerAccountId,
                LocationType = location.LocationType.ToString(),
                RegionId = location.RegionId,
                RegionName = district?.Region?.Name ?? string.Empty,
                DistrictId = location.DistrictId,
                DistrictName = district?.Name,
                LandmarkAndDirections = location.LandmarkAndDirections ?? string.Empty,
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

public class UpdateCustomerLocationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/customers/{customerId:guid}/locations/{locationId:guid}",
            async (Guid customerId, Guid locationId, UpdateCustomerLocation.Command command, ISender sender) =>
            {
                command.CustomerId = customerId;
                command.LocationId = locationId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
            .WithTags("Customers")
            .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
            .WithSummary("Update a customer location")
            .WithDescription("Patches any fields on a location record. Only fields present in the body are applied. Sending isPrimary: true demotes the current primary and promotes this location.")
            .Produces<AddCustomerLocation.LocationResponse>(200)
            .Produces<Error>(404)
            .Produces<Error>(422)
            .RequireAuthorization();
    }
}
