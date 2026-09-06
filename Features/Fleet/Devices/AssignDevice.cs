using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Staff.Enums;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class AssignDevice
{
    public class Command : IRequest<Result<AssignmentResponse>>
    {
        public Guid DeviceId { get; set; }
        public Guid StaffMemberId { get; set; }
        public string? Notes { get; set; }
    }

    public class AssignmentResponse
    {
        public Guid AssignmentId { get; set; }
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public Guid StaffMemberId { get; set; }
        public string StaffName { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; }
        public string? Notes { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.StaffMemberId).NotEmpty();
            RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<AssignmentResponse>>
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

        public async Task<Result<AssignmentResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<AssignmentResponse>(Error.ValidationError(validation));

            var device = await _db.TrackingDevices
                .Include(d => d.Assignments.Where(a => a.UnassignedAt == null))
                .FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken);

            if (device is null)
                return Result.Failure<AssignmentResponse>(Error.CreateNotFoundError("Tracking device not found."));

            if (device.Assignments.Any())
                return Result.Failure<AssignmentResponse>(
                    Error.Conflict("Device is already assigned to a staff member. Unassign it first."));

            var staff = await _db.StaffMembers
                .Include(s => s.DeviceAssignments.Where(a => a.UnassignedAt == null))
                .FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);

            if (staff is null)
                return Result.Failure<AssignmentResponse>(Error.CreateNotFoundError("Staff member not found."));

            if (staff.EmploymentStatus != EmploymentStatus.Active)
                return Result.Failure<AssignmentResponse>(
                    Error.BadRequest("Can only assign a device to an Active staff member."));

            if (staff.DeviceAssignments.Any())
                return Result.Failure<AssignmentResponse>(
                    Error.Conflict("Staff member already has a device assigned. Unassign it first."));

            var assignment = new StaffDeviceAssignment
            {
                StaffMemberId = staff.Id,
                DeviceId = device.Id,
                AssignedAt = DateTime.UtcNow,
                Notes = request.Notes?.Trim()
            };

            _db.StaffDeviceAssignments.Add(assignment);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Device {DeviceId} ({DeviceName}) assigned to staff {StaffId} ({StaffName})",
                device.Id, device.Name, staff.Id, staff.FullName);

            if (device.TraccarDeviceId is not null)
                _ = _traccar.UpdateDeviceAsync(device.TraccarDeviceId.Value, staff.FullName, cancellationToken);

            if (device.TraccarDeviceId is not null && staff.TraccarDriverId is not null)
                _ = _traccar.LinkDriverToDeviceAsync(device.TraccarDeviceId.Value, staff.TraccarDriverId.Value, cancellationToken);

            return Result.Success(new AssignmentResponse
            {
                AssignmentId = assignment.Id,
                DeviceId = device.Id,
                DeviceName = device.Name,
                StaffMemberId = staff.Id,
                StaffName = staff.FullName,
                AssignedAt = assignment.AssignedAt,
                Notes = assignment.Notes
            });
        }
    }
}

public class AssignDeviceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/devices/{deviceId:guid}/assign", async (
            Guid deviceId, AssignDevice.Command command, ISender sender) =>
        {
            command.DeviceId = deviceId;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Assign a tracking device to a staff member")
        .WithDescription(
            "Links a Traccar tracking device to an Active staff member. " +
            "Both the device and the staff member can only have one active assignment at a time.")
        .Produces<AssignDevice.AssignmentResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
