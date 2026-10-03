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

namespace prohpharmacy_trekking_app.Features.Fleet;

public static class GetVehicleStockOverview
{
    public class Query : IRequest<Result<object>>
    {
        public Guid? RegionId { get; set; }
        public Guid? BranchId { get; set; }
        public string? Status { get; set; }
        public string? StockState { get; set; }
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
    }

    public class VehicleStockOverviewItem
    {
        public Guid Id { get; set; }
        public string RegistrationNumber { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Make { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Year { get; set; }
        public string Colour { get; set; } = string.Empty;
        public Guid RegionId { get; set; }
        public string? RegionName { get; set; }
        public Guid? BranchId { get; set; }
        public string? BranchName { get; set; }
        public string OperationalStatus { get; set; } = string.Empty;
        public Guid? CurrentStaffId { get; set; }
        public string? CurrentStaffName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public StockSummary Stock { get; set; } = new();
    }

    public class StockSummary
    {
        public int TrackedProductCount { get; set; }
        public int InStockProductCount { get; set; }
        public int OutOfStockProductCount { get; set; }
        public int LowStockProductCount { get; set; }
        public bool HasStockLoaded { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<object>>
    {
        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.Vehicles
                .Include(v => v.Region)
                .Include(v => v.Branch)
                .Include(v => v.StaffAssignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                .AsNoTracking();

            if (request.RegionId.HasValue)
                query = query.Where(v => v.RegionId == request.RegionId.Value);

            if (request.BranchId.HasValue)
                query = query.Where(v => v.BranchId == request.BranchId.Value);

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<VehicleOperationalStatus>(request.Status, ignoreCase: true, out var parsedStatus))
                    return Result.Failure<object>(Error.BadRequest(
                        $"Invalid status '{request.Status}'. Expected one of: Active, UnderMaintenance, Decommissioned."));
                query = query.Where(v => v.OperationalStatus == parsedStatus);
            }

            if (!string.IsNullOrWhiteSpace(request.StockState))
            {
                var state = request.StockState.Trim().ToLower();
                if (state == "loaded")
                    query = query.Where(v => db.VehicleProductStocks
                        .Any(s => s.VehicleId == v.Id
                               && (s.BasicQuantityOnHand > 0 || s.PackagingQuantityOnHand > 0)));
                else if (state == "empty")
                    query = query.Where(v => !db.VehicleProductStocks
                        .Any(s => s.VehicleId == v.Id
                               && (s.BasicQuantityOnHand > 0 || s.PackagingQuantityOnHand > 0)));
                else
                    return Result.Failure<object>(Error.BadRequest(
                        $"Invalid stockState '{request.StockState}'. Expected one of: loaded, empty."));
            }

            var result = await new QueryBuilder<Vehicle>(query)
                .WithSearch(request.Search,
                    nameof(Vehicle.RegistrationNumber),
                    nameof(Vehicle.DisplayName),
                    nameof(Vehicle.Make),
                    nameof(Vehicle.Model))
                .WithSort(request.Sort ?? "createdAt_desc")
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(v => (object)new VehicleStockOverviewItem
                {
                    Id = v.Id,
                    RegistrationNumber = v.RegistrationNumber,
                    DisplayName = v.DisplayName,
                    Make = v.Make,
                    Model = v.Model,
                    Year = v.Year,
                    Colour = v.Colour,
                    RegionId = v.RegionId,
                    RegionName = v.Region != null ? v.Region.Name : null,
                    BranchId = v.BranchId,
                    BranchName = v.Branch != null ? v.Branch.Name : null,
                    OperationalStatus = v.OperationalStatus.ToString(),
                    CurrentStaffId = v.StaffAssignments.FirstOrDefault() != null
                        ? v.StaffAssignments.First().StaffMemberId
                        : (Guid?)null,
                    CurrentStaffName = v.StaffAssignments.FirstOrDefault() != null
                        ? v.StaffAssignments.First().StaffMember.FullName
                        : null,
                    CreatedAt = v.CreatedAt,
                    UpdatedAt = v.UpdatedAt
                });

            var pageItems = ((Paginator.PaginatedData<object>)result).Data
                .Cast<VehicleStockOverviewItem>()
                .ToList();

            if (pageItems.Count > 0)
            {
                var vehicleIds = pageItems.Select(p => p.Id).ToList();

                var stockRows = await db.VehicleProductStocks
                    .AsNoTracking()
                    .Where(s => vehicleIds.Contains(s.VehicleId))
                    .Select(s => new
                    {
                        s.VehicleId,
                        s.BasicQuantityOnHand,
                        s.PackagingQuantityOnHand,
                        s.LowStockThreshold,
                        s.UpdatedAt,
                        s.CreatedAt
                    })
                    .ToListAsync(cancellationToken);

                var byVehicle = stockRows.GroupBy(s => s.VehicleId).ToDictionary(g => g.Key, g => g.ToList());

                foreach (var item in pageItems)
                {
                    if (!byVehicle.TryGetValue(item.Id, out var rows))
                        continue;

                    var inStock = rows.Count(r => r.BasicQuantityOnHand > 0 || r.PackagingQuantityOnHand > 0);
                    var outOfStock = rows.Count(r => r.BasicQuantityOnHand == 0 && r.PackagingQuantityOnHand == 0);
                    var lowStock = rows.Count(r => r.LowStockThreshold.HasValue
                                                && r.BasicQuantityOnHand <= r.LowStockThreshold.Value);
                    var lastUpdated = rows
                        .Select(r => r.UpdatedAt ?? r.CreatedAt)
                        .DefaultIfEmpty()
                        .Max();

                    item.Stock = new StockSummary
                    {
                        TrackedProductCount = rows.Count,
                        InStockProductCount = inStock,
                        OutOfStockProductCount = outOfStock,
                        LowStockProductCount = lowStock,
                        HasStockLoaded = inStock > 0,
                        LastUpdatedAt = lastUpdated == default ? null : lastUpdated
                    };
                }
            }

            return Result.Success(result);
        }
    }
}

public class GetVehicleStockOverviewEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/fleet/vehicles/stock-overview", async (
            ISender sender,
            [FromQuery] Guid? regionId,
            [FromQuery] Guid? branchId,
            [FromQuery] string? status,
            [FromQuery] string? stockState,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize) =>
        {
            var result = await sender.Send(new GetVehicleStockOverview.Query
            {
                RegionId = regionId,
                BranchId = branchId,
                Status = status,
                StockState = stockState,
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("List vehicles with stock summary")
        .WithDescription("Paginated vehicle list enriched with each vehicle's stock summary (tracked / in-stock / out-of-stock / low-stock counts, hasStockLoaded, lastUpdatedAt). Filter stockState=loaded to show only vehicles with at least one in-stock product, or stockState=empty to show vehicles with no stock loaded. Intended for the vehicle stock management page.")
        .Produces<Paginator.PaginatedData<GetVehicleStockOverview.VehicleStockOverviewItem>>(200)
        .Produces<Error>(400)
        .RequireAuthorization();
    }
}
