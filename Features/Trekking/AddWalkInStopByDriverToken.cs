using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class AddWalkInStopByDriverToken
{
    public class Command : IRequest<Result<TrekStopResponse>>
    {
        public Guid Token { get; set; }
        public Guid TrekId { get; set; }
        public Guid CustomerAccountId { get; set; }
        public int Sequence { get; set; }
        public string? Notes { get; set; }
        public Guid? ClientGeneratedId { get; set; }
        public GpsInput? Gps { get; set; }

        public class GpsInput
        {
            public decimal Latitude { get; set; }
            public decimal Longitude { get; set; }
            public decimal AccuracyMetres { get; set; }
        }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.CustomerAccountId).NotEmpty();
            RuleFor(x => x.Sequence).GreaterThan(0);
            RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
            RuleFor(x => x.Gps!.Latitude).InclusiveBetween(-90, 90).When(x => x.Gps is not null);
            RuleFor(x => x.Gps!.Longitude).InclusiveBetween(-180, 180).When(x => x.Gps is not null);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<TrekStopResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<TrekStopResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TrekStopResponse>(Error.ValidationError(validation));

            var driverTrip = await _db.TrekkingTrips
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);
            if (driverTrip is null)
                return Result.Failure<TrekStopResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var targetTrip = await _db.TrekkingTrips
                .FirstOrDefaultAsync(t => t.Id == request.TrekId && t.RegionId == driverTrip.RegionId, cancellationToken);
            if (targetTrip is null)
                return Result.Failure<TrekStopResponse>(Error.CreateNotFoundError("Trek not found or is not in the same region."));

            if (targetTrip.Status == TrekStatus.Completed || targetTrip.Status == TrekStatus.Cancelled)
                return Result.Failure<TrekStopResponse>(Error.BadRequest($"Cannot add a stop to a {targetTrip.Status} trek."));

            if (targetTrip.Status == TrekStatus.Scheduled)
            {
                targetTrip.Status = TrekStatus.InProgress;
                targetTrip.UpdatedAt = DateTime.UtcNow;
            }

            if (request.ClientGeneratedId.HasValue)
            {
                var duplicate = await _db.TrekkingTripStops
                    .Include(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.Region)
                    .Include(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.Locations.Where(l => l.IsPrimary))
                            .ThenInclude(l => l.District)
                    .Include(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.People.Where(p => p.IsPrimaryContact && p.IsActive))
                    .Include(s => s.Products)
                        .ThenInclude(p => p.Product)
                    .FirstOrDefaultAsync(s => s.TrekkingTripId == request.TrekId
                        && s.CustomerAccountId == request.CustomerAccountId
                        && s.Sequence == request.Sequence, cancellationToken);
                if (duplicate is not null)
                    return Result.Success(GetTrek.Handler.MapStop(duplicate));
            }

            var customer = await _db.CustomerAccounts
                .Include(ca => ca.Region)
                .Include(ca => ca.Locations.Where(l => l.IsPrimary))
                    .ThenInclude(l => l.District)
                .Include(ca => ca.People.Where(p => p.IsPrimaryContact && p.IsActive))
                .FirstOrDefaultAsync(ca => ca.Id == request.CustomerAccountId, cancellationToken);
            if (customer is null)
                return Result.Failure<TrekStopResponse>(Error.CreateNotFoundError("Customer not found."));

            var stop = new TrekkingTripStop
            {
                TrekkingTripId = request.TrekId,
                CustomerAccountId = request.CustomerAccountId,
                Sequence = request.Sequence,
                IsWalkIn = true,
                Notes = request.Notes?.Trim()
            };

            _db.TrekkingTripStops.Add(stop);
            await _db.SaveChangesAsync(cancellationToken);

            var primaryLocation = customer.Locations.FirstOrDefault();
            var primaryContact = customer.People.FirstOrDefault();

            return Result.Success(new TrekStopResponse
            {
                StopId = stop.Id,
                Sequence = stop.Sequence,
                IsWalkIn = stop.IsWalkIn,
                CustomerAccountId = stop.CustomerAccountId,
                CustomerName = customer.BusinessName,
                CustomerCode = customer.CustomerCode,
                CustomerPhone = customer.PrimaryPhoneNumber,
                CustomerType = customer.CustomerType.ToString(),
                RegionName = customer.Region?.Name,
                DistrictName = primaryLocation?.District?.Name,
                PrimaryLocationLandmark = primaryLocation?.LandmarkAndDirections,
                PrimaryLocationStreet = primaryLocation?.StreetAddress,
                PrimaryContactName = primaryContact?.FullName,
                PrimaryContactPhone = primaryContact?.PrimaryPhoneNumber,
                Notes = stop.Notes
            });
        }
    }
}

public class AddWalkInStopByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/treks/{trekId:guid}/stops",
            async (Guid token, Guid trekId, AddWalkInStopByDriverToken.Command command, ISender sender) =>
            {
                command.Token = token;
                command.TrekId = trekId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Created($"api/v1/treks/{trekId}/stops/{result.Value.StopId}", result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Add a walk-in stop to any active trek in the region (driver portal)")
        .Produces<TrekStopResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
