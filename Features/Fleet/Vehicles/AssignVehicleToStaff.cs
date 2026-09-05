using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Staff.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Vehicles;

public static class AssignVehicleToStaff
{
    public class Command : IRequest<Result<AssignmentResponse>>
    {
        public Guid VehicleId { get; set; }
        public Guid StaffMemberId { get; set; }
        public string? Notes { get; set; }
    }

    public class AssignmentResponse
    {
        public Guid AssignmentId { get; set; }
        public Guid VehicleId { get; set; }
        public string VehicleRegistration { get; set; } = string.Empty;
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

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<AssignmentResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<AssignmentResponse>(Error.ValidationError(validation));

            var vehicle = await _db.Vehicles
                .Include(v => v.StaffAssignments.Where(a => a.UnassignedAt == null))
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

            if (vehicle is null)
                return Result.Failure<AssignmentResponse>(Error.CreateNotFoundError("Vehicle not found."));

            if (vehicle.OperationalStatus != Enums.VehicleOperationalStatus.Active)
                return Result.Failure<AssignmentResponse>(
                    Error.BadRequest("Can only assign staff to an Active vehicle."));

            if (vehicle.StaffAssignments.Any())
                return Result.Failure<AssignmentResponse>(
                    Error.Conflict("Vehicle already has a staff member assigned. Unassign first."));

            var staff = await _db.StaffMembers
                .FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);

            if (staff is null)
                return Result.Failure<AssignmentResponse>(Error.CreateNotFoundError("Staff member not found."));

            if (staff.EmploymentStatus != EmploymentStatus.Active)
                return Result.Failure<AssignmentResponse>(
                    Error.BadRequest("Can only assign an Active staff member to a vehicle."));

            var assignment = new VehicleStaffAssignment
            {
                VehicleId = vehicle.Id,
                StaffMemberId = staff.Id,
                AssignedAt = DateTime.UtcNow,
                Notes = request.Notes?.Trim()
            };

            _db.VehicleStaffAssignments.Add(assignment);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new AssignmentResponse
            {
                AssignmentId = assignment.Id,
                VehicleId = vehicle.Id,
                VehicleRegistration = vehicle.RegistrationNumber,
                StaffMemberId = staff.Id,
                StaffName = staff.FullName,
                AssignedAt = assignment.AssignedAt,
                Notes = assignment.Notes
            });
        }
    }
}

public class AssignVehicleToStaffEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/vehicles/{vehicleId:guid}/assign-staff", async (
            Guid vehicleId, AssignVehicleToStaff.Command command, ISender sender) =>
        {
            command.VehicleId = vehicleId;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Assign a staff member to a vehicle")
        .WithDescription(
            "Links an Active staff member to an Active vehicle. " +
            "A vehicle can only have one active staff assignment at a time. " +
            "Unassign the current staff member first if one is already assigned.")
        .RequireAuthorization();
    }
}
