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
                .Include(d => d.Assignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                        .ThenInclude(s => s.Branch)
                .AsNoTracking();

            var devices = await devicesQuery.ToListAsync(cancellationToken);

            var staffIds = devices
                .Select(d => d.Assignments.FirstOrDefault()?.StaffMemberId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            var vehicleAssignments = await _db.VehicleStaffAssignments
                .Where(a => a.UnassignedAt == null && staffIds.Contains(a.StaffMemberId))
                .Include(a => a.Vehicle)
                .AsNoTracking()
                .ToDictionaryAsync(a => a.StaffMemberId, cancellationToken);

            var result = new List<FleetPositionResponse>();

            foreach (var device in devices)
            {
                var staff = device.Assignments.FirstOrDefault()?.StaffMember;

                if (request.BranchId.HasValue && staff?.BranchId != request.BranchId)
                    continue;

                vehicleAssignments.TryGetValue(staff?.Id ?? Guid.Empty, out var vehicleAssignment);

                result.Add(new FleetPositionResponse
                {
                    DeviceId = device.Id,
                    DeviceName = device.Name,
                    StaffMemberId = staff?.Id,
                    StaffName = staff?.FullName,
                    VehicleId = vehicleAssignment?.VehicleId,
                    VehicleRegistration = vehicleAssignment?.Vehicle?.RegistrationNumber,
                    VehicleDisplayName = vehicleAssignment?.Vehicle?.DisplayName,
                    BranchId = staff?.BranchId,
                    BranchName = staff?.Branch?.Name,
                    Latitude = (double)device.LastLatitude!,
                    Longitude = (double)device.LastLongitude!,
                    LastAddress = device.LastAddress,
                    LastReportedAt = device.LastReportedAt!.Value
                });
            }

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
        .RequireAuthorization();
    }
}
