using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Organisation.Localities.CreateLocality;

namespace prohpharmacy_trekking_app.Organisation.Localities;

public static class GetLocalityList
{
    public class Query : IRequest<Result<object>>
    {
        public Guid? DistrictId { get; set; }
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
            var query = _db.Localities
                .Include(l => l.District).ThenInclude(d => d.Region)
                .AsNoTracking();

            if (request.IncludeInactive != true)
                query = query.Where(l => l.IsActive);

            if (request.DistrictId.HasValue)
                query = query.Where(l => l.DistrictId == request.DistrictId.Value);

            if (request.RegionId.HasValue)
                query = query.Where(l => l.District.RegionId == request.RegionId.Value);

            var result = await new QueryBuilder<Entities.Locality>(query)
                .WithSearch(request.Search, nameof(Entities.Locality.Name), nameof(Entities.Locality.Code))
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(l => (object)ToResponse((Entities.Locality)l));

            return Result.Success(result);
        }

        private static LocalityResponse ToResponse(Entities.Locality l) => new()
        {
            Id = l.Id,
            DistrictId = l.DistrictId,
            DistrictName = l.District?.Name ?? string.Empty,
            RegionName = l.District?.Region?.Name ?? string.Empty,
            Code = l.Code,
            Name = l.Name,
            IsActive = l.IsActive,
            CreatedAt = l.CreatedAt
        };
    }

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapGet("api/v1/organisation/localities", async (
                ISender sender,
                [FromQuery] Guid? districtId,
                [FromQuery] Guid? regionId,
                [FromQuery] string? search,
                [FromQuery] string? sort,
                [FromQuery] int? pageNumber,
                [FromQuery] int? pageSize,
                [FromQuery] bool? includeInactive) =>
            {
                var result = await sender.Send(new Query
                {
                    DistrictId = districtId,
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
            .WithTags("Organisation - Localities")
            .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
            .WithSummary("List / search localities — filter by districtId or regionId")
            .RequireAuthorization();
        }
    }
}
