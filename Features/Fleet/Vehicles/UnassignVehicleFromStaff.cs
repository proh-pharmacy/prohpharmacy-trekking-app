using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Vehicles;

public static class UnassignVehicleFromStaff
{
    public class Command : IRequest<Result>
    {
        public Guid VehicleId { get; set; }
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
            var vehicle = await _db.Vehicles
                .Include(v => v.StaffAssignments.Where(a => a.UnassignedAt == null))
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

            if (vehicle is null)
                return Result.Failure(Error.CreateNotFoundError("Vehicle not found."));

            var active = vehicle.StaffAssignments.FirstOrDefault();
            if (active is null)
                return Result.Failure(Error.BadRequest("Vehicle has no active staff assignment."));

            var previousStaffId = active.StaffMemberId;
            active.UnassignedAt = DateTime.UtcNow;

            var device = await _db.TrackingDevices
                .FirstOrDefaultAsync(d => d.VehicleId == vehicle.Id, cancellationToken);

            if (device is not null)
            {
                device.StaffMemberId = null;
                device.Name = vehicle.RegistrationNumber;
                device.UpdatedAt = DateTime.UtcNow;
            }

            var fleetDriver = await _db.FleetDrivers
                .FirstOrDefaultAsync(d => d.StaffMemberId == previousStaffId, cancellationToken);

            if (fleetDriver is not null)
                _db.FleetDrivers.Remove(fleetDriver);

            await _db.SaveChangesAsync(cancellationToken);

            if (device?.TraccarDeviceId is not null)
            {
                _ = _traccar.UpdateDeviceAsync(device.TraccarDeviceId.Value, device.Name, cancellationToken);

                if (fleetDriver?.TraccarDriverId is not null)
                    _ = _traccar.UnlinkDriverFromDeviceAsync(device.TraccarDeviceId.Value, fleetDriver.TraccarDriverId.Value, cancellationToken);
            }

            if (fleetDriver?.TraccarDriverId is not null)
            {
                _ = _traccar.DeleteDriverAsync(fleetDriver.TraccarDriverId.Value, cancellationToken);
                _logger.LogInformation("Fleet driver {StaffId} removed from Traccar #{TraccarId} on vehicle unassignment",
                    previousStaffId, fleetDriver.TraccarDriverId.Value);
            }

            return Result.Success();
        }
    }
}

public class UnassignVehicleFromStaffEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/vehicles/{vehicleId:guid}/unassign-staff", async (
            Guid vehicleId, ISender sender) =>
        {
            var result = await sender.Send(new UnassignVehicleFromStaff.Command { VehicleId = vehicleId });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.NoContent();
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Unassign the current staff member from a vehicle")
        .WithDescription(
            "Ends the active vehicle assignment and removes the staff member as a fleet driver (locally and from Traccar). " +
            "If the vehicle has a registered tracking device, its name reverts to the registration number and the Traccar driver link is removed.")
        .Produces(204)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
