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
        public Guid RegionId { get; set; }
        public Guid? BranchId { get; set; }
        public DateOnly ScheduledDate { get; set; }
        public Guid VehicleId { get; set; }
        public Guid? SalesStaffId { get; set; }
        public string? Notes { get; set; }
    }

    public class TrekResponse
    {
        public Guid Id { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public Guid RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public Guid? BranchId { get; set; }
        public string? BranchName { get; set; }
        public Guid DriverStaffId { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public Guid? SalesStaffId { get; set; }
        public string? SalesStaffName { get; set; }
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
        public bool IsWalkIn { get; set; }
        public Guid CustomerAccountId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string? CustomerPhone { get; set; }
        public string? CustomerType { get; set; }
        public string? RegionName { get; set; }
        public string? DistrictName { get; set; }
        public string? PrimaryLocationLandmark { get; set; }
        public string? PrimaryLocationStreet { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public decimal? AccuracyMetres { get; set; }
        public string? PrimaryContactName { get; set; }
        public string? PrimaryContactPhone { get; set; }
        public string? Notes { get; set; }
        public List<TrekStopProductResponse> Products { get; set; } = [];
        public List<TrekStopReturnResponse> Returns { get; set; } = [];
    }

    public class TrekStopProductResponse
    {
        public Guid StopProductId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? BasicUnitName { get; set; }
        public string? PackagingUnitName { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public decimal PlannedBasicQuantity { get; set; }
        public decimal? PlannedPackagingQuantity { get; set; }
        public decimal? BasicQtyDelivered { get; set; }
        public decimal? PackagingQtyDelivered { get; set; }
        public string? PaymentMethod { get; set; }
        public decimal? AmtPaid { get; set; }
        public decimal? Balance { get; set; }
        public bool IsUnplanned { get; set; }
        public string? Notes { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }

    public class TrekStopReturnResponse
    {
        public Guid ReturnId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? BasicUnitName { get; set; }
        public string? PackagingUnitName { get; set; }
        public decimal BasicQtyReturned { get; set; }
        public decimal? PackagingQtyReturned { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public decimal? RefundAmount { get; set; }
        public string? RefundMethod { get; set; }
        public string? Reason { get; set; }
        public DateTime RecordedAt { get; set; }
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

            var region = await _db.Regions.FindAsync([request.RegionId], cancellationToken);
            if (region is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Region not found."));

            string? branchName = null;
            if (request.BranchId.HasValue)
            {
                var branch = await _db.Branches.FindAsync([request.BranchId.Value], cancellationToken);
                if (branch is null)
                    return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Branch not found."));
                branchName = branch.Name;
            }

            var vehicle = await _db.Vehicles
                .Include(v => v.StaffAssignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);
            if (vehicle is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Vehicle not found."));

            var activeAssignment = vehicle.StaffAssignments.FirstOrDefault();
            if (activeAssignment is null)
                return Result.Failure<TrekResponse>(Error.BadRequest("Vehicle has no active driver assigned. Please assign a staff member to this vehicle before creating a trek."));

            var driver = activeAssignment.StaffMember;

            var userId = _auth.GetUserId();
            if (userId is null)
                return Result.Failure<TrekResponse>(Error.BadRequest("Unable to determine authenticated user."));

            string? salesStaffName = null;
            if (request.SalesStaffId.HasValue)
            {
                var salesStaff = await _db.StaffMembers.FindAsync([request.SalesStaffId.Value], cancellationToken);
                if (salesStaff is null)
                    return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Sales staff member not found."));
                salesStaffName = salesStaff.FullName;
            }

            var seq = await _db.Database.SqlQueryRaw<long>("SELECT nextval('\"TrekNumberSequence\"') AS \"Value\"").FirstAsync(cancellationToken);
            var trekNumber = $"TRK-{seq:D5}";

            var trip = new TrekkingTrip
            {
                TrekNumber = trekNumber,
                RegionId = request.RegionId,
                BranchId = request.BranchId,
                ScheduledDate = request.ScheduledDate,
                DriverStaffId = driver.Id,
                SalesStaffId = request.SalesStaffId,
                VehicleId = request.VehicleId,
                Status = TrekStatus.Draft,
                Notes = request.Notes?.Trim(),
                CreatedBy = Guid.Parse(userId),
                CreatedAt = DateTime.UtcNow
            };

            _db.TrekkingTrips.Add(trip);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(trip, region.Name, branchName, driver.FullName, salesStaffName, vehicle.DisplayName, []));
        }

        internal static TrekResponse ToResponse(
            TrekkingTrip trip,
            string regionName,
            string? branchName,
            string driverName,
            string? salesStaffName,
            string vehicleDisplayName,
            List<TrekStopResponse> stops) => new()
        {
            Id = trip.Id,
            TrekNumber = trip.TrekNumber,
            RegionId = trip.RegionId,
            RegionName = regionName,
            BranchId = trip.BranchId,
            BranchName = branchName,
            DriverStaffId = trip.DriverStaffId,
            DriverName = driverName,
            SalesStaffId = trip.SalesStaffId,
            SalesStaffName = salesStaffName,
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
