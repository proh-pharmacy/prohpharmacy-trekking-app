using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
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

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var device = await _db.TrackingDevices
                .Include(d => d.Assignments.Where(a => a.UnassignedAt == null))
                .FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken);

            if (device is null)
                return Result.Failure(Error.CreateNotFoundError("Tracking device not found."));

            var active = device.Assignments.FirstOrDefault();
            if (active is null)
                return Result.Failure(Error.BadRequest("Device has no active staff assignment."));

            active.UnassignedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

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
