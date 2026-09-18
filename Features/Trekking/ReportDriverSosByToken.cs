using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class ReportDriverSosByToken
{
    public class Command : IRequest<Result>
    {
        public Guid Token { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Altitude { get; set; }
        public double? Accuracy { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
            RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
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
                accuracy: request.Accuracy,
                alarm: "sos",
                ct: cancellationToken);

            return Result.Success();
        }
    }
}

public class ReportDriverSosByTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/sos",
            async (Guid token, ReportDriverSosByToken.Command command, ISender sender) =>
            {
                command.Token = token;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.NoContent();
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Send SOS alert from driver portal")
        .WithDescription("Reports a position event to Traccar with alarm=sos. Traccar will fire an alarm event which triggers any configured notifications (push, email, SMS). The driver's current GPS coordinates must be included.")
        .Produces(204)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
