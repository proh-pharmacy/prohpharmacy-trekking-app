using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class SyncDevicesToTraccar
{
    public class Command : IRequest<Result<SyncResponse>> { }

    public class SyncResponse
    {
        public int Synced { get; set; }
        public int Failed { get; set; }
        public int AlreadySynced { get; set; }
        public List<string> Errors { get; set; } = [];
    }

    internal sealed class Handler : IRequestHandler<Command, Result<SyncResponse>>
    {
        private readonly AppDbContext _db;
        private readonly ITraccarService _traccar;

        public Handler(AppDbContext db, ITraccarService traccar)
        {
            _db = db;
            _traccar = traccar;
        }

        public async Task<Result<SyncResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var devices = await _db.TrackingDevices.ToListAsync(cancellationToken);

            var response = new SyncResponse
            {
                AlreadySynced = devices.Count(d => d.TraccarDeviceId is not null)
            };

            var unsynced = devices.Where(d => d.TraccarDeviceId is null).ToList();

            foreach (var device in unsynced)
            {
                var traccarDevice = await _traccar.CreateDeviceAsync(device.Name, device.TraccarUniqueId, cancellationToken);
                if (traccarDevice is not null)
                {
                    device.TraccarDeviceId = traccarDevice.Id;
                    device.UpdatedAt = DateTime.UtcNow;
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

            return Result.Success(response);
        }
    }
}

public class SyncDevicesToTraccarEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/devices/sync", async (ISender sender) =>
        {
            var result = await sender.Send(new SyncDevicesToTraccar.Command());
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Sync unregistered devices to Traccar")
        .WithDescription("Registers any tracking devices missing a Traccar device ID. Already synced devices are skipped.")
        .RequireAuthorization();
    }
}
