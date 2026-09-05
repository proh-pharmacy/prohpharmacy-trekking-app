using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Organisation.Regions.CreateRegion;

namespace prohpharmacy_trekking_app.Organisation.Regions;

public static class GetRegionList
{
    public class Query : IRequest<Result<object>>
    {
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
            var query = _db.Regions.AsNoTracking();

            if (request.IncludeInactive != true)
                query = query.Where(r => r.IsActive);

            var result = await new QueryBuilder<Entities.Region>(query)
                .WithSearch(request.Search, nameof(Entities.Region.Name), nameof(Entities.Region.Code))
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(r => (object)ToResponse(r));

            return Result.Success(result);
        }

        private static RegionResponse ToResponse(Entities.Region r) => new()
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            IsActive = r.IsActive,
            CreatedAt = r.CreatedAt
        };
    }
}

public class GetRegionListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/organisation/regions", async (
            ISender sender,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromQuery] bool? includeInactive) =>
        {
            var result = await sender.Send(new GetRegionList.Query
            {
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
        .WithTags("Organisation - Regions")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("List / search regions")
        .RequireAuthorization();
    }
}
