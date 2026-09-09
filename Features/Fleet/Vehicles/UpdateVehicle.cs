using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Fleet.Vehicles.CreateVehicle;

namespace prohpharmacy_trekking_app.Features.Fleet.Vehicles;

public static class UpdateVehicle
{
    public class Command : IRequest<Result<VehicleResponse>>
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Make { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Year { get; set; }
        public string Colour { get; set; } = string.Empty;
        public Guid? BranchId { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.Make).NotEmpty().MaximumLength(80);
            RuleFor(x => x.Model).NotEmpty().MaximumLength(80);
            RuleFor(x => x.Year).InclusiveBetween(1990, DateTime.UtcNow.Year + 1)
                .WithMessage($"Year must be between 1990 and {DateTime.UtcNow.Year + 1}.");
            RuleFor(x => x.Colour).NotEmpty().MaximumLength(50);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<VehicleResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<VehicleResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<VehicleResponse>(Error.ValidationError(validation));

            var vehicle = await _db.Vehicles
                .Include(v => v.Branch)
                .Include(v => v.StaffAssignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                .FirstOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (vehicle is null)
                return Result.Failure<VehicleResponse>(Error.CreateNotFoundError("Vehicle not found."));

            string? branchName = vehicle.Branch?.Name;
            if (request.BranchId.HasValue)
            {
                var branch = await _db.Branches.FindAsync([request.BranchId.Value], cancellationToken);
                if (branch is null)
                    return Result.Failure<VehicleResponse>(Error.CreateNotFoundError("Branch not found."));
                if (!branch.IsActive)
                    return Result.Failure<VehicleResponse>(Error.BadRequest("Cannot assign vehicle to an inactive branch."));
                vehicle.BranchId = request.BranchId.Value;
                branchName = branch.Name;
            }
            else
            {
                vehicle.BranchId = null;
                branchName = null;
            }

            vehicle.DisplayName = request.DisplayName.Trim();
            vehicle.Make = request.Make.Trim();
            vehicle.Model = request.Model.Trim();
            vehicle.Year = request.Year;
            vehicle.Colour = request.Colour.Trim();
            vehicle.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            var activeStaff = vehicle.StaffAssignments.FirstOrDefault();

            return Result.Success(CreateVehicle.Handler.ToResponse(
                vehicle,
                branchName,
                activeStaff?.StaffMemberId,
                activeStaff?.StaffMember?.FullName));
        }
    }
}

public class UpdateVehicleEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("api/v1/fleet/vehicles/{id:guid}", async (Guid id, UpdateVehicle.Command command, ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Update a vehicle")
        .WithDescription("Updates vehicle details. Registration number cannot be changed after creation.")
        .Produces<VehicleResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
