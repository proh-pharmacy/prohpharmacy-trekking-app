using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Features.Organisation.Regions.SetRegionalMarkup;

namespace prohpharmacy_trekking_app.Features.Organisation.Regions;

public static class GetRegionalMarkupList
{
    public class Query : IRequest<Result<object>>
    {
        public Guid RegionId { get; set; }
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<object>>
    {
        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var regionExists = await db.Regions.AnyAsync(r => r.Id == request.RegionId, cancellationToken);
            if (!regionExists)
                return Result.Failure<object>(Error.CreateNotFoundError("Region not found."));

            var query = db.RegionalMarkupRules
                .Include(r => r.Region)
                .Include(r => r.Product)
                .Where(r => r.RegionId == request.RegionId)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(request.Search))
                query = query.Where(r => r.ProductId == null ||
                    r.Product!.Name.ToLower().Contains(request.Search.ToLower()));

            query = query
                .OrderBy(r => r.ProductId == null ? 0 : 1)
                .ThenBy(r => r.Product!.Name);

            var result = await new QueryBuilder<RegionalMarkupRule>(query)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(r => (object)new MarkupRuleResponse
                {
                    Id = r.Id,
                    RegionId = r.RegionId,
                    RegionName = r.Region.Name,
                    ProductId = r.ProductId,
                    ProductName = r.Product?.Name,
                    MarkupPercentage = r.MarkupPercentage,
                    CreatedAt = r.CreatedAt,
                    UpdatedAt = r.UpdatedAt
                });

            return Result.Success(result);
        }
    }
}

public class GetRegionalMarkupListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/organisation/regions/{regionId:guid}/markups",
            async (
                Guid regionId,
                ISender sender,
                [FromQuery] string? search,
                [FromQuery] string? sort,
                [FromQuery] int? pageNumber,
                [FromQuery] int? pageSize) =>
            {
                var result = await sender.Send(new GetRegionalMarkupList.Query
                {
                    RegionId = regionId,
                    Search = search,
                    Sort = sort,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Organisation - Regions")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("List markup rules for a region")
        .WithDescription(
            "Returns paginated markup rules for the given region. " +
            "The region-wide rule (no ProductId) always appears first. " +
            "Search filters by product name.")
        .Produces<Paginator.PaginatedData<SetRegionalMarkup.MarkupRuleResponse>>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
