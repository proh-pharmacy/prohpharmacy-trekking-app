using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Organisation.Districts.CreateDistrict;

namespace prohpharmacy_trekking_app.Organisation.Districts;

public static class GetDistrictList
{
    public class Query : IRequest<Result<object>>
    {
        public Guid? RegionId { get; set; }
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
            var query = _db.Districts
                .Include(d => d.Region)
                .AsNoTracking();

            if (request.IncludeInactive != true)
                query = query.Where(d => d.IsActive);

            if (request.RegionId.HasValue)
                query = query.Where(d => d.RegionId == request.RegionId.Value);

            var result = await new QueryBuilder<Entities.District>(query)
                .WithSearch(request.Search, nameof(Entities.District.Name), nameof(Entities.District.Code))
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(d => (object)ToResponse((Entities.District)d));

            return Result.Success(result);
        }

        private static DistrictResponse ToResponse(Entities.District d) => new()
        {
            Id = d.Id,
            RegionId = d.RegionId,
            RegionName = d.Region?.Name ?? string.Empty,
            Code = d.Code,
            Name = d.Name,
            IsActive = d.IsActive,
            CreatedAt = d.CreatedAt
        };
    }
}

public class GetDistrictListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/organisation/districts", async (
            ISender sender,
            [FromQuery] Guid? regionId,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromQuery] bool? includeInactive) =>
        {
            var result = await sender.Send(new GetDistrictList.Query
            {
                RegionId = regionId,
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
        .WithTags("Organisation - Districts")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("List / search districts — optionally filter by regionId")
        .RequireAuthorization();
    }
}
