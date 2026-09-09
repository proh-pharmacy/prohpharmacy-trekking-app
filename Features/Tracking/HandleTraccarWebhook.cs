using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Hubs;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Tracking;

public static class HandleTraccarWebhook
{
    public class Command : IRequest<Result>
    {
        public TraccarWebhookPayload Payload { get; set; } = new();
    }

    internal sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<TrackingHub> _hub;

        public Handler(AppDbContext db, IHubContext<TrackingHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var position = request.Payload.Position;
            if (position is null || !position.Valid)
                return Result.Success();

            var traccarDeviceId = position.DeviceId;

            var device = await _db.TrackingDevices
                .Include(d => d.StaffMember)
                    .ThenInclude(s => s!.Branch)
                .Include(d => d.Vehicle)
                .FirstOrDefaultAsync(d => d.TraccarDeviceId == traccarDeviceId, cancellationToken);

            if (device is null)
                return Result.Success();

            device.LastLatitude = (decimal)position.Latitude;
            device.LastLongitude = (decimal)position.Longitude;
            device.LastReportedAt = position.FixTime;
            if (!string.IsNullOrEmpty(position.Address))
                device.LastAddress = position.Address;
            await _db.SaveChangesAsync(cancellationToken);

            var staff = device.StaffMember;

            var broadcast = new PositionBroadcast
            {
                DeviceId = device.Id,
                TraccarDeviceId = traccarDeviceId,
                StaffMemberId = device.StaffMemberId,
                StaffName = staff?.FullName,
                VehicleId = device.VehicleId,
                VehicleRegistration = device.Vehicle?.RegistrationNumber,
                BranchId = staff?.BranchId,
                BranchName = staff?.Branch?.Name,
                Latitude = position.Latitude,
                Longitude = position.Longitude,
                Speed = position.Speed,
                Course = position.Course,
                FixTime = position.FixTime,
                Valid = position.Valid,
                Ignition = position.Ignition,
                Motion = position.Motion,
                BatteryLevel = position.BatteryLevel,
                Address = position.Address
            };

            if (staff?.BranchId is not null)
                await _hub.Clients.Group($"branch-{staff.BranchId}").SendAsync("PositionUpdated", broadcast, cancellationToken);

            await _hub.Clients.All.SendAsync("PositionUpdated", broadcast, cancellationToken);

            return Result.Success();
        }
    }
}

public class TraccarWebhookEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/traccar/webhook", async (
            [FromQuery] string? secret,
            TraccarWebhookPayload payload,
            ISender sender,
            IConfiguration config) =>
        {
            var expected = config.GetValue<string>("TraccarSettings:WebhookSecret");
            if (!string.IsNullOrEmpty(expected) && secret != expected)
                return Results.Unauthorized();

            await sender.Send(new HandleTraccarWebhook.Command { Payload = payload });
            return Results.Ok();
        })
        .WithTags("Tracking")
        .Produces(200)
        .Produces(401)
        .ExcludeFromDescription();
    }
}
