using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class CreateTrackingDevice
{
    public class Command : IRequest<Result<DeviceResponse>>
    {
        public Guid VehicleId { get; set; }
        public string? UniqueId { get; set; }
        public string? PhoneNumber { get; set; }
    }

    public class DeviceResponse
    {
        public Guid Id { get; set; }
        public int? TraccarDeviceId { get; set; }
        public string TraccarUniqueId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? VehicleId { get; set; }
        public string? VehicleRegistration { get; set; }
        public Guid? StaffMemberId { get; set; }
        public string? StaffName { get; set; }
        public DateTime? LastReportedAt { get; set; }
        public decimal? LastLatitude { get; set; }
        public decimal? LastLongitude { get; set; }
        public string? LastAddress { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.VehicleId).NotEmpty();
            RuleFor(x => x.UniqueId).MaximumLength(50).When(x => x.UniqueId is not null);
            RuleFor(x => x.PhoneNumber).MaximumLength(30).When(x => x.PhoneNumber is not null);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<DeviceResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;
        private readonly ITraccarService _traccar;
        private readonly ILogger<Handler> _logger;

        public Handler(AppDbContext db, IValidator<Command> validator, ITraccarService traccar, ILogger<Handler> logger)
        {
            _db = db;
            _validator = validator;
            _traccar = traccar;
            _logger = logger;
        }

        public async Task<Result<DeviceResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<DeviceResponse>(Error.ValidationError(validation));

            var vehicle = await _db.Vehicles
                .Include(v => v.StaffAssignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

            if (vehicle is null)
                return Result.Failure<DeviceResponse>(Error.CreateNotFoundError("Vehicle not found."));

            var alreadyHasDevice = await _db.TrackingDevices
                .AnyAsync(d => d.VehicleId == request.VehicleId, cancellationToken);

            if (alreadyHasDevice)
                return Result.Failure<DeviceResponse>(Error.Conflict("This vehicle already has a tracking device registered."));

            var activeAssignment = vehicle.StaffAssignments.FirstOrDefault();
            var staff = activeAssignment?.StaffMember;

            var deviceName = staff is not null
                ? $"{staff.FullName} - {vehicle.RegistrationNumber}"
                : vehicle.RegistrationNumber;

            var device = new TrackingDevice
            {
                TraccarUniqueId = request.UniqueId?.Trim() ?? Guid.NewGuid().ToString("N"),
                Name = deviceName,
                PhoneNumber = request.PhoneNumber?.Trim(),
                Status = TrackingDeviceStatus.Active,
                VehicleId = vehicle.Id,
                StaffMemberId = staff?.Id,
                CreatedAt = DateTime.UtcNow
            };

            var traccarDevice = await _traccar.CreateDeviceAsync(device.Name, device.TraccarUniqueId, cancellationToken);
            if (traccarDevice is not null)
            {
                device.TraccarDeviceId = traccarDevice.Id;
                _logger.LogInformation("Tracking device {DeviceId} ({Name}) registered in Traccar as #{TraccarId}",
                    device.Id, device.Name, traccarDevice.Id);
            }
            else
            {
                _logger.LogWarning("Tracking device {DeviceId} ({Name}) created locally but Traccar registration failed — sync manually",
                    device.Id, device.Name);
            }

            _db.TrackingDevices.Add(device);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(device, vehicle.RegistrationNumber, staff?.FullName));
        }

        internal static DeviceResponse ToResponse(TrackingDevice d, string? vehicleRegistration, string? staffName) => new()
        {
            Id = d.Id,
            TraccarDeviceId = d.TraccarDeviceId,
            TraccarUniqueId = d.TraccarUniqueId,
            Name = d.Name,
            PhoneNumber = d.PhoneNumber,
            Status = d.Status.ToString(),
            VehicleId = d.VehicleId,
            VehicleRegistration = vehicleRegistration ?? d.Vehicle?.RegistrationNumber,
            StaffMemberId = d.StaffMemberId,
            StaffName = staffName ?? d.StaffMember?.FullName,
            LastReportedAt = d.LastReportedAt,
            LastLatitude = d.LastLatitude,
            LastLongitude = d.LastLongitude,
            LastAddress = d.LastAddress,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt
        };
    }
}

public class CreateTrackingDeviceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/devices", async (CreateTrackingDevice.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/fleet/devices/{result.Value.Id}", result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Register a tracking device for a vehicle")
        .WithDescription(
            "Registers a GPS tracking device for a vehicle and immediately syncs to Traccar. " +
            "Device name is auto-set to '{StaffName} - {RegistrationNumber}' if the vehicle has a driver assigned, otherwise just the registration number. " +
            "**Smartphone:** omit `uniqueId` — a UUID is auto-generated. " +
            "**Hardware GPS tracker:** supply the IMEI as `uniqueId`.")
        .Produces<CreateTrackingDevice.DeviceResponse>(201)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
