using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Features.Organisation.Branches.CreateBranch;

namespace prohpharmacy_trekking_app.Features.Organisation.Branches;

public static class GetBranchList
{
    public class Query : IRequest<Result<object>>
    {
        public Guid? RegionId { get; set; }
        public Guid? DistrictId { get; set; }
        public Guid? LocalityId { get; set; }
        public string? BranchType { get; set; }
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public bool? IncludeInactive { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<object>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = _db.Branches
                .Include(b => b.Region)
                .Include(b => b.District)
                .Include(b => b.Locality)
                .AsNoTracking();

            if (request.IncludeInactive != true)
                query = query.Where(b => b.IsActive);

            if (request.RegionId.HasValue)
                query = query.Where(b => b.RegionId == request.RegionId.Value);

            if (request.DistrictId.HasValue)
                query = query.Where(b => b.DistrictId == request.DistrictId.Value);

            if (request.LocalityId.HasValue)
                query = query.Where(b => b.LocalityId == request.LocalityId.Value);

            if (!string.IsNullOrWhiteSpace(request.BranchType))
                query = query.Where(b => b.BranchType.ToString().ToLower() == request.BranchType.ToLower());

            var result = await new QueryBuilder<Entities.Branch>(query)
                .WithSearch(request.Search, nameof(Entities.Branch.Name), nameof(Entities.Branch.Code))
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(b => (object)ToResponse((Entities.Branch)b));

            return Result.Success(result);
        }

        private static BranchResponse ToResponse(Entities.Branch b) => new()
        {
            Id = b.Id,
            Code = b.Code,
            Name = b.Name,
            BranchType = b.BranchType.ToString(),
            RegionId = b.RegionId,
            RegionName = b.Region?.Name ?? string.Empty,
            DistrictId = b.DistrictId,
            DistrictName = b.District?.Name ?? string.Empty,
            LocalityId = b.LocalityId,
            LocalityName = b.Locality?.Name ?? string.Empty,
            Address = b.Address,
            Latitude = b.Latitude,
            Longitude = b.Longitude,
            ContactNumber = b.ContactNumber,
            IsActive = b.IsActive,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt
        };
    }
}

public class GetBranchListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/organisation/branches", async (
            ISender sender,
            [FromQuery] Guid? regionId,
            [FromQuery] Guid? districtId,
            [FromQuery] Guid? localityId,
            [FromQuery] string? branchType,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromQuery] bool? includeInactive) =>
        {
            var result = await sender.Send(new GetBranchList.Query
            {
                RegionId = regionId,
                DistrictId = districtId,
                LocalityId = localityId,
                BranchType = branchType,
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize,
                IncludeInactive = includeInactive
            });

            return result.IsFailure
                ? Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Organisation - Branches")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("List / search branches")
        .WithDescription("Filter by regionId, districtId, localityId, or branchType (Retail | Wholesale | Laboratory).")
        .RequireAuthorization();
    }
}
