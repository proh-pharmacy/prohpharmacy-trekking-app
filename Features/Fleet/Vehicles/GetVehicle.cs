using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Fleet.Vehicles.CreateVehicle;

namespace prohpharmacy_trekking_app.Features.Fleet.Vehicles;

public static class GetVehicle
{
    public class Query : IRequest<Result<VehicleResponse>>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<VehicleResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<VehicleResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var vehicle = await _db.Vehicles
                .Include(v => v.Branch)
                .Include(v => v.StaffAssignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (vehicle is null)
                return Result.Failure<VehicleResponse>(Error.CreateNotFoundError("Vehicle not found."));

            var activeStaff = vehicle.StaffAssignments.FirstOrDefault();

            return Result.Success(CreateVehicle.Handler.ToResponse(
                vehicle,
                vehicle.Branch?.Name,
                activeStaff?.StaffMemberId,
                activeStaff?.StaffMember?.FullName));
        }
    }
}

public class GetVehicleEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/fleet/vehicles/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetVehicle.Query { Id = id });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Get a vehicle by ID")
        .RequireAuthorization();
    }
}
