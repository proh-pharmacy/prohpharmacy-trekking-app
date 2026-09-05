using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class SyncDevicesToTraccar
{
    public class Command : IRequest<Result<SyncResponse>>
    {
        public bool Force { get; set; }
    }

    public class SyncResponse
    {
        public int Synced { get; set; }
        public int Failed { get; set; }
        public int AlreadySynced { get; set; }
        public int Deleted { get; set; }
        public List<string> Errors { get; set; } = [];
    }

    internal sealed class Handler : IRequestHandler<Command, Result<SyncResponse>>
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

        public async Task<Result<SyncResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var devices = await _db.TrackingDevices.ToListAsync(cancellationToken);
            var response = new SyncResponse();

            List<int> traccarIdsBefore = [];
            if (request.Force)
            {
                var allTraccar = await _traccar.GetAllDevicesAsync(cancellationToken);
                traccarIdsBefore = allTraccar.Select(d => d.Id).ToList();

                foreach (var d in devices)
                {
                    d.TraccarDeviceId = null;
                    d.UpdatedAt = DateTime.UtcNow;
                }
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                response.AlreadySynced = devices.Count(d => d.TraccarDeviceId is not null);
            }

            var unsynced = devices.Where(d => d.TraccarDeviceId is null).ToList();
            var syncedTraccarIds = new HashSet<int>();

            foreach (var device in unsynced)
            {
                var traccarDevice = await _traccar.CreateDeviceAsync(device.Name, device.TraccarUniqueId, cancellationToken);
                if (traccarDevice is not null)
                {
                    device.TraccarDeviceId = traccarDevice.Id;
                    device.UpdatedAt = DateTime.UtcNow;
                    syncedTraccarIds.Add(traccarDevice.Id);
                    response.Synced++;
                }
                else
                {
                    response.Failed++;
                    response.Errors.Add($"Failed to sync device '{device.Name}' (id: {device.Id}).");
                }
            }

            if (response.Synced > 0)
                await _db.SaveChangesAsync(cancellationToken);

            if (request.Force)
            {
                foreach (var orphanId in traccarIdsBefore.Where(id => !syncedTraccarIds.Contains(id)))
                {
                    var deleted = await _traccar.DeleteDeviceAsync(orphanId, cancellationToken);
                    if (deleted)
                        response.Deleted++;
                    else
                        response.Errors.Add($"Failed to delete orphan Traccar device (traccarId: {orphanId}).");
                }
            }

            _logger.LogInformation(
                "Device sync complete — synced: {Synced}, already synced: {AlreadySynced}, failed: {Failed}, deleted: {Deleted}",
                response.Synced, response.AlreadySynced, response.Failed, response.Deleted);

            foreach (var err in response.Errors)
                _logger.LogWarning("Device sync error: {Error}", err);

            return Result.Success(response);
        }
    }
}

public class SyncDevicesToTraccarEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/devices/sync", async (
            [FromQuery] bool force,
            ISender sender) =>
        {
            var result = await sender.Send(new SyncDevicesToTraccar.Command { Force = force });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Sync devices to Traccar")
        .WithDescription(
            "Registers tracking devices in Traccar. " +
            "By default only syncs devices missing a Traccar ID. " +
            "Use `?force=true` to clear all stored Traccar IDs, re-register everything, and delete any orphan devices that exist in Traccar but not in the local database — useful after spawning a fresh Traccar instance.")
        .RequireAuthorization();
    }
}
