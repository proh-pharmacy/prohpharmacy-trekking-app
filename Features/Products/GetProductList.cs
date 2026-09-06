using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Features.Products.CreateProduct;

namespace prohpharmacy_trekking_app.Features.Products;

public static class GetProductList
{
    public class Query : IRequest<Result<object>>
    {
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public bool? IsActive { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<object>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = _db.Products.AsNoTracking();

            if (request.IsActive.HasValue)
                query = query.Where(p => p.IsActive == request.IsActive.Value);

            var result = await new QueryBuilder<Entities.Product>(query)
                .WithSearch(request.Search, nameof(Entities.Product.Name))
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(p => (object)CreateProduct.Handler.ToResponse(p));

            return Result.Success(result);
        }
    }
}

public class GetProductListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/products", async (
            ISender sender,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromQuery] bool? isActive) =>
        {
            var result = await sender.Send(new GetProductList.Query
            {
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize,
                IsActive = isActive
            });

            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Products")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("List / search products")
        .Produces<Paginator.PaginatedData<ProductResponse>>(200)
        .RequireAuthorization();
    }
}
