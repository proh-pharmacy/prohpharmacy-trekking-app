using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Features.Fleet.Vehicles.CreateVehicle;

namespace prohpharmacy_trekking_app.Features.Fleet.Vehicles;

public static class GetVehicleList
{
    public class Query : IRequest<Result<object>>
    {
        public Guid? BranchId { get; set; }
        public string? Status { get; set; }
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<object>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = _db.Vehicles
                .Include(v => v.Branch)
                .Include(v => v.StaffAssignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                .AsNoTracking();

            if (request.BranchId.HasValue)
                query = query.Where(v => v.BranchId == request.BranchId.Value);

            if (!string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<VehicleOperationalStatus>(request.Status, ignoreCase: true, out var parsedStatus))
                query = query.Where(v => v.OperationalStatus == parsedStatus);

            var result = await new QueryBuilder<Vehicle>(query)
                .WithSearch(request.Search,
                    nameof(Vehicle.RegistrationNumber),
                    nameof(Vehicle.DisplayName),
                    nameof(Vehicle.Make),
                    nameof(Vehicle.Model))
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(v =>
                {
                    var activeStaff = v.StaffAssignments.FirstOrDefault();
                    return (object)CreateVehicle.Handler.ToResponse(
                        v,
                        v.Branch?.Name,
                        activeStaff?.StaffMemberId,
                        activeStaff?.StaffMember?.FullName);
                });

            return Result.Success(result);
        }
    }
}

public class GetVehicleListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/fleet/vehicles", async (
            ISender sender,
            [FromQuery] Guid? branchId,
            [FromQuery] string? status,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize) =>
        {
            var result = await sender.Send(new GetVehicleList.Query
            {
                BranchId = branchId,
                Status = status,
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize
            });

            return result.IsFailure
                ? Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("List / search vehicles")
        .WithDescription("Filter by branchId or status (Active | UnderMaintenance | Decommissioned).")
        .Produces<Paginator.PaginatedData<VehicleResponse>>(200)
        .RequireAuthorization();
    }
}
