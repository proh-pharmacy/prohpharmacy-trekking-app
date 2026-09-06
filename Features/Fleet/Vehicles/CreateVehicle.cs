using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Vehicles;

public static class CreateVehicle
{
    public class Command : IRequest<Result<VehicleResponse>>
    {
        public string RegistrationNumber { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Make { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Year { get; set; }
        public string Colour { get; set; } = string.Empty;
        public Guid BranchId { get; set; }
    }

    public class VehicleResponse
    {
        public Guid Id { get; set; }
        public string RegistrationNumber { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Make { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Year { get; set; }
        public string Colour { get; set; } = string.Empty;
        public Guid BranchId { get; set; }
        public string? BranchName { get; set; }
        public string OperationalStatus { get; set; } = string.Empty;
        public Guid? CurrentStaffId { get; set; }
        public string? CurrentStaffName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.RegistrationNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.Make).NotEmpty().MaximumLength(80);
            RuleFor(x => x.Model).NotEmpty().MaximumLength(80);
            RuleFor(x => x.Year).InclusiveBetween(1990, DateTime.UtcNow.Year + 1)
                .WithMessage($"Year must be between 1990 and {DateTime.UtcNow.Year + 1}.");
            RuleFor(x => x.Colour).NotEmpty().MaximumLength(50);
            RuleFor(x => x.BranchId).NotEmpty();
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

            var branch = await _db.Branches.FindAsync([request.BranchId], cancellationToken);
            if (branch is null)
                return Result.Failure<VehicleResponse>(Error.CreateNotFoundError("Branch not found."));

            if (!branch.IsActive)
                return Result.Failure<VehicleResponse>(Error.BadRequest("Cannot assign vehicle to an inactive branch."));

            var regTaken = await _db.Vehicles
                .AnyAsync(v => v.RegistrationNumber == request.RegistrationNumber.Trim().ToUpper(), cancellationToken);
            if (regTaken)
                return Result.Failure<VehicleResponse>(Error.Conflict("Registration number already exists."));

            var vehicle = new Vehicle
            {
                RegistrationNumber = request.RegistrationNumber.Trim().ToUpper(),
                DisplayName = request.DisplayName.Trim(),
                Make = request.Make.Trim(),
                Model = request.Model.Trim(),
                Year = request.Year,
                Colour = request.Colour.Trim(),
                BranchId = request.BranchId,
                OperationalStatus = VehicleOperationalStatus.Active,
                CreatedAt = DateTime.UtcNow
            };

            _db.Vehicles.Add(vehicle);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(vehicle, branch.Name, null, null));
        }

        internal static VehicleResponse ToResponse(
            Vehicle v, string? branchName,
            Guid? currentStaffId, string? currentStaffName) => new()
        {
            Id = v.Id,
            RegistrationNumber = v.RegistrationNumber,
            DisplayName = v.DisplayName,
            Make = v.Make,
            Model = v.Model,
            Year = v.Year,
            Colour = v.Colour,
            BranchId = v.BranchId,
            BranchName = branchName,
            OperationalStatus = v.OperationalStatus.ToString(),
            CurrentStaffId = currentStaffId,
            CurrentStaffName = currentStaffName,
            CreatedAt = v.CreatedAt,
            UpdatedAt = v.UpdatedAt
        };
    }
}

public class CreateVehicleEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/vehicles", async (CreateVehicle.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/fleet/vehicles/{result.Value.Id}", result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Register a new vehicle")
        .WithDescription("Adds a vehicle to the fleet. Registration number is normalised to uppercase and must be unique.")
        .Produces<CreateVehicle.VehicleResponse>(201)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
