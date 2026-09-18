using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class ReportDriverLocationByToken
{
    public class Command : IRequest<Result>
    {
        public Guid Token { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Altitude { get; set; }
        public double? Speed { get; set; }
        public double? Bearing { get; set; }
        public double? Accuracy { get; set; }
        public double? BatteryLevel { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
            RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
            RuleFor(x => x.BatteryLevel).InclusiveBetween(0.0, 1.0).When(x => x.BatteryLevel.HasValue);
        }
    }

    internal sealed class Handler(AppDbContext db, ITraccarService traccar) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var device = await db.TrackingDevices
                .AsNoTracking()
                .Where(d => d.VehicleId == trip.VehicleId || d.StaffMemberId == trip.DriverStaffId)
                .OrderByDescending(d => d.VehicleId != null)
                .FirstOrDefaultAsync(cancellationToken);

            if (device is null)
                return Result.Failure(Error.CreateNotFoundError("No tracking device is registered for this trek's vehicle or driver."));

            await traccar.ReportPositionAsync(
                device.TraccarUniqueId,
                request.Latitude, request.Longitude,
                altitude: request.Altitude,
                speed: request.Speed,
                bearing: request.Bearing,
                accuracy: request.Accuracy,
                batteryLevel: request.BatteryLevel,
                ct: cancellationToken);

            return Result.Success();
        }
    }
}

public class ReportDriverLocationByTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/location",
            async (Guid token, ReportDriverLocationByToken.Command command, ISender sender) =>
            {
                command.Token = token;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.NoContent();
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Report driver GPS location (driver portal)")
        .WithDescription("Forwards the driver's current GPS coordinates to Traccar via the OsmAnd protocol. Call this on a regular interval while online. BatteryLevel is 0.0–1.0 (from navigator.getBattery()); Speed is m/s (from Geolocation API).")
        .Produces(204)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
