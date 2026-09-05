using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Tracking;

public static class GetDevicePosition
{
    public class Query : IRequest<Result<PositionResponse>>
    {
        public Guid DeviceId { get; set; }
    }

    public class PositionResponse
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public Guid? StaffMemberId { get; set; }
        public string? StaffName { get; set; }
        public Guid? VehicleId { get; set; }
        public string? VehicleRegistration { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Speed { get; set; }
        public double Course { get; set; }
        public string? Address { get; set; }
        public bool? Ignition { get; set; }
        public bool? Motion { get; set; }
        public double? BatteryLevel { get; set; }
        public DateTime FixTime { get; set; }
        public bool Valid { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<PositionResponse>>
    {
        private readonly AppDbContext _db;
        private readonly ITraccarService _traccar;

        public Handler(AppDbContext db, ITraccarService traccar)
        {
            _db = db;
            _traccar = traccar;
        }

        public async Task<Result<PositionResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var device = await _db.TrackingDevices
                .Include(d => d.Assignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken);

            if (device is null)
                return Result.Failure<PositionResponse>(Error.CreateNotFoundError("Tracking device not found."));

            if (device.TraccarDeviceId is null)
                return Result.Failure<PositionResponse>(Error.BadRequest("Device is not registered in Traccar."));

            var position = await _traccar.GetCurrentPositionAsync(device.TraccarDeviceId.Value, cancellationToken);
            if (position is null)
                return Result.Failure<PositionResponse>(Error.CreateNotFoundError("No position data available for this device yet."));

            var staff = device.Assignments.FirstOrDefault()?.StaffMember;

            Guid? vehicleId = null;
            string? vehicleRegistration = null;

            if (staff is not null)
            {
                var vehicleAssignment = await _db.VehicleStaffAssignments
                    .Include(a => a.Vehicle)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.StaffMemberId == staff.Id && a.UnassignedAt == null, cancellationToken);

                vehicleId = vehicleAssignment?.VehicleId;
                vehicleRegistration = vehicleAssignment?.Vehicle?.RegistrationNumber;
            }

            return Result.Success(new PositionResponse
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                StaffMemberId = staff?.Id,
                StaffName = staff?.FullName,
                VehicleId = vehicleId,
                VehicleRegistration = vehicleRegistration,
                Latitude = position.Latitude,
                Longitude = position.Longitude,
                Speed = position.Speed,
                Course = position.Course,
                Address = position.Address,
                Ignition = position.Ignition,
                Motion = position.Motion,
                BatteryLevel = position.BatteryLevel,
                FixTime = position.FixTime,
                Valid = position.Valid
            });
        }
    }
}

public class GetDevicePositionEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/fleet/devices/{id:guid}/position", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetDevicePosition.Query { DeviceId = id });
            return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Tracking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Tracking)
        .WithSummary("Get current position for a device")
        .WithDescription("Fetches the latest GPS position directly from Traccar for the given device, including the currently assigned staff member and vehicle.")
        .RequireAuthorization();
    }
}
