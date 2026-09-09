using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Drivers;

public static class GetFleetDriverList
{
    public class Query : IRequest<Result<List<FleetDriverResponse>>>
    {
        public bool? Synced { get; set; }
    }

    public class FleetDriverResponse
    {
        public Guid Id { get; set; }
        public Guid StaffMemberId { get; set; }
        public string StaffName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? BranchName { get; set; }
        public int? TraccarDriverId { get; set; }
        public string? TraccarUniqueId { get; set; }
        public bool IsSynced { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<List<FleetDriverResponse>>>
    {
        public async Task<Result<List<FleetDriverResponse>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.FleetDrivers
                .Include(d => d.StaffMember)
                    .ThenInclude(s => s.Branch)
                .AsNoTracking();

            if (request.Synced.HasValue)
                query = request.Synced.Value
                    ? query.Where(d => d.TraccarDriverId != null)
                    : query.Where(d => d.TraccarDriverId == null);

            var drivers = await query.OrderBy(d => d.CreatedAt).ToListAsync(cancellationToken);

            return Result.Success(drivers.Select(d => ToResponse(d, d.StaffMember)).ToList());
        }

        internal static FleetDriverResponse ToResponse(FleetDriver d, StaffMember s) => new()
        {
            Id = d.Id,
            StaffMemberId = d.StaffMemberId,
            StaffName = s.FullName,
            PhoneNumber = s.PhoneNumber,
            BranchName = s.Branch?.Name,
            TraccarDriverId = d.TraccarDriverId,
            TraccarUniqueId = d.TraccarUniqueId,
            IsSynced = d.TraccarDriverId.HasValue,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt
        };
    }
}

public class GetFleetDriverListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/fleet/drivers", async (
            ISender sender,
            [FromQuery] bool? synced) =>
        {
            var result = await sender.Send(new GetFleetDriverList.Query { Synced = synced });
            return result.IsFailure
                ? Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("List fleet drivers")
        .WithDescription("Returns all registered fleet drivers. Filter by `synced=true` to see only those synced with Traccar.")
        .Produces<List<GetFleetDriverList.FleetDriverResponse>>(200)
        .RequireAuthorization();
    }
}
