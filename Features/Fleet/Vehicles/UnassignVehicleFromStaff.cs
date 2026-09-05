using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Vehicles;

public static class UnassignVehicleFromStaff
{
    public class Command : IRequest<Result>
    {
        public Guid VehicleId { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var vehicle = await _db.Vehicles
                .Include(v => v.StaffAssignments.Where(a => a.UnassignedAt == null))
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

            if (vehicle is null)
                return Result.Failure(Error.CreateNotFoundError("Vehicle not found."));

            var active = vehicle.StaffAssignments.FirstOrDefault();
            if (active is null)
                return Result.Failure(Error.BadRequest("Vehicle has no active staff assignment."));

            active.UnassignedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public class UnassignVehicleFromStaffEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/vehicles/{vehicleId:guid}/unassign-staff", async (
            Guid vehicleId, ISender sender) =>
        {
            var result = await sender.Send(new UnassignVehicleFromStaff.Command { VehicleId = vehicleId });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.NoContent();
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Unassign the current staff member from a vehicle")
        .RequireAuthorization();
    }
}
