using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class UnassignDevice
{
    public class Command : IRequest<Result>
    {
        public Guid DeviceId { get; set; }
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
                .Include(d => d.Assignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                .FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken);

            if (device is null)
                return Result.Failure(Error.CreateNotFoundError("Tracking device not found."));

            var active = device.Assignments.FirstOrDefault();
            if (active is null)
                return Result.Failure(Error.BadRequest("Device has no active staff assignment."));

            var staff = active.StaffMember;

            active.UnassignedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Device {DeviceId} unassigned from staff {StaffId} (assignment {AssignmentId})",
                device.Id, active.StaffMemberId, active.Id);

            if (device.TraccarDeviceId is not null && staff?.TraccarDriverId is not null)
                _ = _traccar.UnlinkDriverFromDeviceAsync(device.TraccarDeviceId.Value, staff.TraccarDriverId.Value, cancellationToken);

            return Result.Success();
        }
    }
}

public class UnassignDeviceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/devices/{deviceId:guid}/unassign", async (
            Guid deviceId, ISender sender) =>
        {
            var result = await sender.Send(new UnassignDevice.Command { DeviceId = deviceId });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.NoContent();
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Unassign the tracking device from its current staff member")
        .RequireAuthorization();
    }
}
