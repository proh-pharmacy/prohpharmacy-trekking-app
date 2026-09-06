using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Features.Staff.Enums;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class CreateTrackingDevice
{
    public class Command : IRequest<Result<DeviceResponse>>
    {
        public Guid StaffMemberId { get; set; }
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
        public DateTime? LastReportedAt { get; set; }
        public decimal? LastLatitude { get; set; }
        public decimal? LastLongitude { get; set; }
        public string? LastAddress { get; set; }
        public Guid? CurrentStaffId { get; set; }
        public string? CurrentStaffName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.StaffMemberId).NotEmpty();
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

            var staff = await _db.StaffMembers
                .Include(s => s.DeviceAssignments.Where(a => a.UnassignedAt == null))
                .FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);

            if (staff is null)
                return Result.Failure<DeviceResponse>(Error.CreateNotFoundError("Staff member not found."));

            if (staff.EmploymentStatus != EmploymentStatus.Active)
                return Result.Failure<DeviceResponse>(
                    Error.BadRequest("Can only register a device for an Active staff member."));

            if (staff.DeviceAssignments.Any())
                return Result.Failure<DeviceResponse>(
                    Error.Conflict("Staff member already has a device assigned. Unassign it first."));

            var device = new TrackingDevice
            {
                TraccarUniqueId = request.UniqueId?.Trim() ?? Guid.NewGuid().ToString("N"),
                Name = staff.FullName,
                PhoneNumber = request.PhoneNumber?.Trim(),
                Status = TrackingDeviceStatus.Active,
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

            _db.StaffDeviceAssignments.Add(new StaffDeviceAssignment
            {
                StaffMemberId = staff.Id,
                DeviceId = device.Id,
                AssignedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(device, staff.Id, staff.FullName));
        }

        internal static DeviceResponse ToResponse(
            TrackingDevice d, Guid? currentStaffId, string? currentStaffName) => new()
        {
            Id = d.Id,
            TraccarDeviceId = d.TraccarDeviceId,
            TraccarUniqueId = d.TraccarUniqueId,
            Name = d.Name,
            PhoneNumber = d.PhoneNumber,
            Status = d.Status.ToString(),
            LastReportedAt = d.LastReportedAt,
            LastLatitude = d.LastLatitude,
            LastLongitude = d.LastLongitude,
            LastAddress = d.LastAddress,
            CurrentStaffId = currentStaffId,
            CurrentStaffName = currentStaffName,
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
        .WithSummary("Register a tracking device for a staff member")
        .WithDescription(
            "Registers a GPS tracking device and immediately assigns it to the given staff member. " +
            "**Smartphone:** omit `uniqueId` — a UUID is auto-generated and returned as `traccarUniqueId`. The staff member pastes this into the Device Identifier field in the Traccar Client app. " +
            "**Hardware GPS tracker:** supply the device IMEI as `uniqueId` and configure the tracker to send to `tracking.prohpharmacy.com` on the correct protocol port.")
        .Produces<CreateTrackingDevice.DeviceResponse>(201)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
