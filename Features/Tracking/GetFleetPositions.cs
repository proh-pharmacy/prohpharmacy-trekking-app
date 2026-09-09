using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Tracking;

public static class GetFleetPositions
{
    public class Query : IRequest<Result<List<FleetPositionResponse>>>
    {
        public Guid? BranchId { get; set; }
    }

    public class FleetPositionResponse
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public Guid? StaffMemberId { get; set; }
        public string? StaffName { get; set; }
        public Guid? VehicleId { get; set; }
        public string? VehicleRegistration { get; set; }
        public string? VehicleDisplayName { get; set; }
        public Guid? BranchId { get; set; }
        public string? BranchName { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? LastAddress { get; set; }
        public DateTime LastReportedAt { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<List<FleetPositionResponse>>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<List<FleetPositionResponse>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var devicesQuery = _db.TrackingDevices
                .Where(d => d.LastLatitude != null && d.LastLongitude != null && d.LastReportedAt != null)
                .Include(d => d.StaffMember)
                    .ThenInclude(s => s!.Branch)
                .Include(d => d.Vehicle)
                .AsNoTracking();

            if (request.BranchId.HasValue)
                devicesQuery = devicesQuery.Where(d => d.StaffMember != null && d.StaffMember.BranchId == request.BranchId.Value);

            var devices = await devicesQuery.ToListAsync(cancellationToken);

            var result = devices.Select(d => new FleetPositionResponse
            {
                DeviceId = d.Id,
                DeviceName = d.Name,
                StaffMemberId = d.StaffMemberId,
                StaffName = d.StaffMember?.FullName,
                VehicleId = d.VehicleId,
                VehicleRegistration = d.Vehicle?.RegistrationNumber,
                VehicleDisplayName = d.Vehicle?.DisplayName,
                BranchId = d.StaffMember?.BranchId,
                BranchName = d.StaffMember?.Branch?.Name,
                Latitude = (double)d.LastLatitude!,
                Longitude = (double)d.LastLongitude!,
                LastAddress = d.LastAddress,
                LastReportedAt = d.LastReportedAt!.Value
            }).ToList();

            return Result.Success(result);
        }
    }
}

public class GetFleetPositionsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/fleet/positions", async (
            ISender sender,
            [Microsoft.AspNetCore.Mvc.FromQuery] Guid? branchId) =>
        {
            var result = await sender.Send(new GetFleetPositions.Query { BranchId = branchId });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Tracking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Tracking)
        .WithSummary("Get last known positions for all active devices")
        .WithDescription(
            "Returns the last known position for every device that has reported in. " +
            "Uses cached data updated by the Traccar webhook — no live Traccar call is made. " +
            "Optionally filter by `branchId` to get only devices belonging to a specific branch.")
        .Produces<List<GetFleetPositions.FleetPositionResponse>>(200)
        .RequireAuthorization();
    }
}
