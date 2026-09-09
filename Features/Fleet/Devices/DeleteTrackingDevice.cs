using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class DeleteTrackingDevice
{
    public class Command : IRequest<Result>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly AppDbContext _db;
        private readonly ITraccarService _traccar;
        private readonly ILogger<Handler> _logger;

        public Handler(AppDbContext db, ITraccarService traccar, ILogger<Handler> logger)
        {
            _db = db;
            _traccar = traccar;
            _logger = logger;
        }

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var device = await _db.TrackingDevices
                .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);

            if (device is null)
                return Result.Failure(Error.CreateNotFoundError("Tracking device not found."));

            if (device.TraccarDeviceId is not null)
            {
                var deleted = await _traccar.DeleteDeviceAsync(device.TraccarDeviceId.Value, cancellationToken);
                if (!deleted)
                    _logger.LogWarning("Failed to delete device #{TraccarDeviceId} from Traccar — removing from local DB anyway",
                        device.TraccarDeviceId.Value);
                else
                    _logger.LogInformation("Device {DeviceId} ({Name}) deleted from Traccar #{TraccarDeviceId}",
                        device.Id, device.Name, device.TraccarDeviceId.Value);
            }

            _db.TrackingDevices.Remove(device);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public class DeleteTrackingDeviceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/fleet/devices/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new DeleteTrackingDevice.Command { Id = id });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.NoContent();
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Delete a tracking device")
        .WithDescription("Removes the device from Traccar and deletes it from the local database.")
        .Produces(204)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
