using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Customers.AddCustomerLocation;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class AddCustomerLocationByDriverToken
{
    public class Command : IRequest<Result<LocationResponse>>
    {
        public Guid Token { get; set; }
        public Guid CustomerId { get; set; }
        public LocationType LocationType { get; set; } = LocationType.BusinessPremises;
        public Guid? DistrictId { get; set; }
        public string? StreetAddress { get; set; }
        public string? LandmarkAndDirections { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public decimal? AccuracyMetres { get; set; }
        public bool IsPrimary { get; set; }
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

            var trip = await db.TrekkingTrips
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<LocationResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var account = await db.CustomerAccounts
                .FirstOrDefaultAsync(a => a.Id == request.CustomerId &&
                    (a.RegionId == trip.RegionId || a.Locations.Any(l => l.RegionId == trip.RegionId)), cancellationToken);

            if (account is null)
                return Result.Failure<LocationResponse>(Error.CreateNotFoundError("Customer not found in this trek's region."));

            var attributedStaffId = trip.SalesStaffId ?? trip.DriverStaffId;

            var hasGps = request.Latitude.HasValue && request.Longitude.HasValue;
            var captureMethod = hasGps ? CaptureMethod.PwaGps : CaptureMethod.ManualLocationSelection;
            var verificationStatus = hasGps ? LocationVerificationStatus.GpsCaptured : LocationVerificationStatus.Unverified;

            var location = new CustomerLocation
            {
                CustomerAccountId = account.Id,
                LocationType = request.LocationType,
                RegionId = trip.RegionId,
                DistrictId = request.DistrictId,
                StreetAddress = request.StreetAddress?.Trim(),
                LandmarkAndDirections = request.LandmarkAndDirections?.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                AccuracyMetres = request.AccuracyMetres,
                CaptureMethod = captureMethod,
                VerificationStatus = verificationStatus,
                IsPrimary = request.IsPrimary,
                CapturedByStaffId = attributedStaffId,
                CreatedAt = DateTime.UtcNow
            };

            if (request.IsPrimary)
            {
                await db.CustomerLocations
                    .Where(l => l.CustomerAccountId == account.Id && l.IsPrimary)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsPrimary, false), cancellationToken);
            }

            db.CustomerLocations.Add(location);
            account.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(BuildResponse(location, null));
        }

        private static LocationResponse BuildResponse(CustomerLocation l, string? districtName) => new()
        {
            Id = l.Id,
            CustomerAccountId = l.CustomerAccountId,
            LocationType = l.LocationType.ToString(),
            RegionId = l.RegionId,
            RegionName = string.Empty,
            DistrictId = l.DistrictId,
            DistrictName = districtName,
            LandmarkAndDirections = l.LandmarkAndDirections ?? string.Empty,
            StreetAddress = l.StreetAddress,
            Latitude = l.Latitude,
            Longitude = l.Longitude,
            AccuracyMetres = l.AccuracyMetres,
            CaptureMethod = l.CaptureMethod.ToString(),
            VerificationStatus = l.VerificationStatus.ToString(),
            IsPrimary = l.IsPrimary,
            CreatedAt = l.CreatedAt
        };
    }
}

public class AddCustomerLocationByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/customers/{customerId:guid}/locations",
            async (Guid token, Guid customerId, AddCustomerLocationByDriverToken.Command command, ISender sender) =>
            {
                command.Token = token;
                command.CustomerId = customerId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Created($"api/v1/customers/{customerId}/locations/{result.Value.Id}", result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Add an additional location to a customer (driver portal)")
        .WithDescription("Adds a new location record to an existing customer. The customer must belong to the trek's region. districtId is optional unlike the admin equivalent. Response shape is identical to POST /api/v1/customers/{customerId}/locations.")
        .Produces<LocationResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
