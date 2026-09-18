using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetDriverDeviceByToken
{
    public class Query : IRequest<Result<DriverDeviceResponse>>
    {
        public Guid Token { get; set; }
    }

    public class DriverDeviceResponse
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string TraccarUniqueId { get; set; } = string.Empty;
        public decimal? LastLatitude { get; set; }
        public decimal? LastLongitude { get; set; }
        public string? LastAddress { get; set; }
        public DateTime? LastReportedAt { get; set; }
        public double? BatteryLevel { get; set; }
        public double? Speed { get; set; }
        public bool? Motion { get; set; }
        public bool? Ignition { get; set; }
        public string? TraccarStatus { get; set; }
    }

    internal sealed class Handler(AppDbContext db, ITraccarService traccar) : IRequestHandler<Query, Result<DriverDeviceResponse>>
    {
        public async Task<Result<DriverDeviceResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<DriverDeviceResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var device = await db.TrackingDevices
                .AsNoTracking()
                .Where(d => d.VehicleId == trip.VehicleId || d.StaffMemberId == trip.DriverStaffId)
                .OrderByDescending(d => d.VehicleId != null)
                .FirstOrDefaultAsync(cancellationToken);

            if (device is null)
                return Result.Failure<DriverDeviceResponse>(Error.CreateNotFoundError("No tracking device is registered for this trek's vehicle or driver."));

            var response = new DriverDeviceResponse
            {
                DeviceId        = device.Id,
                DeviceName      = device.Name,
                TraccarUniqueId = device.TraccarUniqueId,
                LastLatitude    = device.LastLatitude,
                LastLongitude   = device.LastLongitude,
                LastAddress     = device.LastAddress,
                LastReportedAt  = device.LastReportedAt
            };

            if (device.TraccarDeviceId.HasValue)
            {
                var position = await traccar.GetCurrentPositionAsync(device.TraccarDeviceId.Value, cancellationToken);
                if (position is not null)
                {
                    response.BatteryLevel = position.BatteryLevel;
                    response.Speed        = position.Speed;
                    response.Motion       = position.Motion;
                    response.Ignition     = position.Ignition;
                }

                var traccarDevice = await traccar.GetDeviceAsync(device.TraccarDeviceId.Value, cancellationToken);
                if (traccarDevice is not null)
                    response.TraccarStatus = traccarDevice.Status;
            }

            return Result.Success(response);
        }
    }
}

public class GetDriverDeviceByTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/device",
            async (Guid token, ISender sender) =>
            {
                var result = await sender.Send(new GetDriverDeviceByToken.Query { Token = token });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get tracking device info for the driver's trek (driver portal)")
        .WithDescription("Returns the last known GPS position, battery level, speed, motion state, and Traccar status for the vehicle or personal device linked to this trek. Battery and live fields come directly from Traccar; lastLatitude/lastLongitude come from the webhook cache.")
        .Produces<GetDriverDeviceByToken.DriverDeviceResponse>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
