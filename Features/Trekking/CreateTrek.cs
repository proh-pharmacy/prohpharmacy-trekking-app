using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class CreateTrek
{
    public class Command : IRequest<Result<TrekResponse>>
    {
        public Guid BranchId { get; set; }
        public DateOnly ScheduledDate { get; set; }
        public Guid DriverStaffId { get; set; }
        public Guid VehicleId { get; set; }
        public string? Notes { get; set; }
    }

    public class TrekResponse
    {
        public Guid Id { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public Guid DriverStaffId { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public Guid VehicleId { get; set; }
        public string VehicleDisplayName { get; set; } = string.Empty;
        public DateOnly ScheduledDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<TrekStopResponse> Stops { get; set; } = [];
    }

    public class TrekStopResponse
    {
        public Guid StopId { get; set; }
        public int Sequence { get; set; }
        public Guid CustomerAccountId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string? PrimaryLocationLandmark { get; set; }
        public string? PrimaryLocationStreet { get; set; }
        public string? Notes { get; set; }
        public List<TrekStopProductResponse> Products { get; set; } = [];
    }

    public class TrekStopProductResponse
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public decimal PlannedQuantity { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.BranchId).NotEmpty();
            RuleFor(x => x.ScheduledDate).NotEmpty();
            RuleFor(x => x.DriverStaffId).NotEmpty();
            RuleFor(x => x.VehicleId).NotEmpty();
            RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<TrekResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;
        private readonly AuthProvider _auth;

        public Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth)
        {
            _db = db;
            _validator = validator;
            _auth = auth;
        }

        public async Task<Result<TrekResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TrekResponse>(Error.ValidationError(validation));

            var branch = await _db.Branches.FindAsync([request.BranchId], cancellationToken);
            if (branch is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Branch not found."));

            var driver = await _db.StaffMembers.FindAsync([request.DriverStaffId], cancellationToken);
            if (driver is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Driver staff member not found."));

            var vehicle = await _db.Vehicles.FindAsync([request.VehicleId], cancellationToken);
            if (vehicle is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Vehicle not found."));

            var userId = _auth.GetUserId();
            if (userId is null)
                return Result.Failure<TrekResponse>(Error.BadRequest("Unable to determine authenticated user."));

            var tripCount = await _db.TrekkingTrips.CountAsync(cancellationToken);
            var trekNumber = $"TRK-{tripCount + 1:D5}";

            var trip = new TrekkingTrip
            {
                TrekNumber = trekNumber,
                BranchId = request.BranchId,
                ScheduledDate = request.ScheduledDate,
                DriverStaffId = request.DriverStaffId,
                VehicleId = request.VehicleId,
                Status = TrekStatus.Draft,
                Notes = request.Notes?.Trim(),
                CreatedBy = Guid.Parse(userId),
                CreatedAt = DateTime.UtcNow
            };

            _db.TrekkingTrips.Add(trip);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(trip, branch.Name, driver.FullName, vehicle.DisplayName, []));
        }

        internal static TrekResponse ToResponse(
            TrekkingTrip trip,
            string branchName,
            string driverName,
            string vehicleDisplayName,
            List<TrekStopResponse> stops) => new()
        {
            Id = trip.Id,
            TrekNumber = trip.TrekNumber,
            BranchId = trip.BranchId,
            BranchName = branchName,
            DriverStaffId = trip.DriverStaffId,
            DriverName = driverName,
            VehicleId = trip.VehicleId,
            VehicleDisplayName = vehicleDisplayName,
            ScheduledDate = trip.ScheduledDate,
            Status = trip.Status.ToString(),
            Notes = trip.Notes,
            CreatedAt = trip.CreatedAt,
            UpdatedAt = trip.UpdatedAt,
            Stops = stops
        };
    }
}

public class CreateTrekEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks", async (CreateTrek.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/treks/{result.Value.Id}", result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Create a new trekking trip")
        .Produces<CreateTrek.TrekResponse>(201)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
