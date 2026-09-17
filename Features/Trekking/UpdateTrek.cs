using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class UpdateTrek
{
    public class Command : IRequest<Result<TrekResponse>>
    {
        public Guid Id { get; set; }
        public Guid RegionId { get; set; }
        public Guid? BranchId { get; set; }
        public DateOnly ScheduledDate { get; set; }
        public Guid VehicleId { get; set; }
        public Guid? SalesStaffId { get; set; }
        public string? Notes { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.RegionId).NotEmpty();
            RuleFor(x => x.ScheduledDate).NotEmpty();
            RuleFor(x => x.VehicleId).NotEmpty();
            RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator)
        : IRequestHandler<Command, Result<TrekResponse>>
    {
        public async Task<Result<TrekResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TrekResponse>(Error.ValidationError(validation));

            var trip = await db.TrekkingTrips
                .Include(t => t.Region)
                .Include(t => t.Branch)
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .Include(t => t.Vehicle)
                .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

            if (trip is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            if (trip.Status == Enums.TrekStatus.Completed || trip.Status == Enums.TrekStatus.Cancelled)
                return Result.Failure<TrekResponse>(Error.BadRequest($"A {trip.Status} trek cannot be updated."));

            var region = await db.Regions.FindAsync([request.RegionId], cancellationToken);
            if (region is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Region not found."));

            string? branchName = null;
            if (request.BranchId.HasValue)
            {
                var branch = await db.Branches.FindAsync([request.BranchId.Value], cancellationToken);
                if (branch is null)
                    return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Branch not found."));
                branchName = branch.Name;
            }

            var vehicle = await db.Vehicles
                .Include(v => v.StaffAssignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

            if (vehicle is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Vehicle not found."));

            var activeAssignment = vehicle.StaffAssignments.FirstOrDefault();
            if (activeAssignment is null)
                return Result.Failure<TrekResponse>(Error.BadRequest("Vehicle has no active driver assigned. Please assign a staff member to this vehicle before updating the trek."));

            string? salesStaffName = null;
            if (request.SalesStaffId.HasValue)
            {
                var salesStaff = await db.StaffMembers.FindAsync([request.SalesStaffId.Value], cancellationToken);
                if (salesStaff is null)
                    return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Sales staff member not found."));
                salesStaffName = salesStaff.FullName;
            }

            trip.RegionId = request.RegionId;
            trip.BranchId = request.BranchId;
            trip.ScheduledDate = request.ScheduledDate;
            trip.VehicleId = request.VehicleId;
            trip.DriverStaffId = activeAssignment.StaffMember.Id;
            trip.SalesStaffId = request.SalesStaffId;
            trip.Notes = request.Notes?.Trim();
            trip.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(CreateTrek.Handler.ToResponse(
                trip,
                region.Name,
                branchName,
                activeAssignment.StaffMember.FullName,
                salesStaffName,
                vehicle.DisplayName,
                []));
        }
    }
}

public class UpdateTrekEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/treks/{id:guid}", async (Guid id, UpdateTrek.Command command, ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Update trek details")
        .WithDescription(
            "Updates region, branch (optional), scheduled date, vehicle, and notes. The driver is re-inferred from the vehicle's active staff assignment. " +
            "Returns 422 if the vehicle has no active driver or if the trek is Completed or Cancelled.")
        .Produces<CreateTrek.TrekResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
