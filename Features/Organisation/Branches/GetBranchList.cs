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
                .AsNoTracking();

            if (request.IncludeInactive != true)
                query = query.Where(b => b.IsActive);

            if (request.RegionId.HasValue)
                query = query.Where(b => b.RegionId == request.RegionId.Value);

            if (request.DistrictId.HasValue)
                query = query.Where(b => b.DistrictId == request.DistrictId.Value);

            if (!string.IsNullOrWhiteSpace(request.BranchType))
                query = query.Where(b => b.BranchType.ToString().ToLower() == request.BranchType.ToLower());

            var result = await new QueryBuilder<Entities.Branch>(query)
                .WithSearch(request.Search, nameof(Entities.Branch.Name), nameof(Entities.Branch.Code))
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(b => (object)CreateBranch.Handler.ToResponse(
                    (Entities.Branch)b,
                    ((Entities.Branch)b).Region?.Name ?? string.Empty,
                    ((Entities.Branch)b).District?.Name ?? string.Empty));

            return Result.Success(result);
        }
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
        .WithDescription("Filter by regionId, districtId, or branchType (Retail | Wholesale | Laboratory).")
        .RequireAuthorization();
    }
}
