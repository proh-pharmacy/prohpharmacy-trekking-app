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
        public int AlreadySynced { get; set; }
        public int Failed { get; set; }
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
            var dbChanged = false;

            if (request.Force)
            {
                // Full reconciliation:
                // 1. Fetch everything from Traccar and index by UniqueId
                // 2. Match local devices by UniqueId — re-link if found, create if not
                // 3. Delete any Traccar device with no matching local UniqueId (true orphans)

                var allTraccar = await _traccar.GetAllDevicesAsync(cancellationToken);
                var traccarByUniqueId = allTraccar.ToDictionary(d => d.UniqueId, d => d.Id);
                var knownTraccarIds = new HashSet<int>();

                foreach (var device in devices)
                {
                    if (traccarByUniqueId.TryGetValue(device.TraccarUniqueId, out var existingTraccarId))
                    {
                        // Already in Traccar — just ensure local ID is correct
                        knownTraccarIds.Add(existingTraccarId);

                        if (device.TraccarDeviceId != existingTraccarId)
                        {
                            device.TraccarDeviceId = existingTraccarId;
                            device.UpdatedAt = DateTime.UtcNow;
                            dbChanged = true;
                            _logger.LogInformation("Device {DeviceId} ({Name}) re-linked to existing Traccar entry #{TraccarId}",
                                device.Id, device.Name, existingTraccarId);
                        }

                        response.AlreadySynced++;
                    }
                    else
                    {
                        // Not in Traccar — create it
                        var traccarDevice = await _traccar.CreateDeviceAsync(device.Name, device.TraccarUniqueId, cancellationToken);
                        if (traccarDevice is not null)
                        {
                            device.TraccarDeviceId = traccarDevice.Id;
                            device.UpdatedAt = DateTime.UtcNow;
                            knownTraccarIds.Add(traccarDevice.Id);
                            dbChanged = true;
                            response.Synced++;
                        }
                        else
                        {
                            response.Failed++;
                            response.Errors.Add($"Failed to sync device '{device.Name}' (id: {device.Id}).");
                        }
                    }
                }

                // Delete orphans — Traccar entries with no matching local device
                foreach (var orphan in allTraccar.Where(d => !knownTraccarIds.Contains(d.Id)))
                {
                    var deleted = await _traccar.DeleteDeviceAsync(orphan.Id, cancellationToken);
                    if (deleted)
                    {
                        response.Deleted++;
                        _logger.LogInformation("Deleted orphan Traccar device #{TraccarId} ({Name})", orphan.Id, orphan.Name);
                    }
                    else
                    {
                        response.Errors.Add($"Failed to delete orphan Traccar device #{orphan.Id} ({orphan.Name}).");
                    }
                }
            }
            else
            {
                // Light sync: only create Traccar entries for local devices that have none
                response.AlreadySynced = devices.Count(d => d.TraccarDeviceId is not null);
                var unsynced = devices.Where(d => d.TraccarDeviceId is null).ToList();

                foreach (var device in unsynced)
                {
                    var traccarDevice = await _traccar.CreateDeviceAsync(device.Name, device.TraccarUniqueId, cancellationToken);
                    if (traccarDevice is not null)
                    {
                        device.TraccarDeviceId = traccarDevice.Id;
                        device.UpdatedAt = DateTime.UtcNow;
                        dbChanged = true;
                        response.Synced++;
                    }
                    else
                    {
                        response.Failed++;
                        response.Errors.Add($"Failed to sync device '{device.Name}' (id: {device.Id}).");
                    }
                }
            }

            if (dbChanged)
                await _db.SaveChangesAsync(cancellationToken);

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
            "Default (`force=false`): creates Traccar entries only for devices that have none. " +
            "With `force=true`: performs a full reconciliation — re-links any device already in Traccar by its unique ID, " +
            "creates any that are missing, and deletes Traccar entries that have no matching local device.")
        .Produces<SyncDevicesToTraccar.SyncResponse>(200)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
