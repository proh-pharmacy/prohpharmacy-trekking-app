using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Features.Customers.SetCustomerMarkup;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class GetAllCustomerMarkupList
{
    public class Query : IRequest<Result<object>>
    {
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? ProductId { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<object>>
    {
        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.CustomerMarkupRules
                .Include(r => r.CustomerAccount)
                .Include(r => r.Product)
                .AsNoTracking();

            if (request.CustomerId.HasValue)
                query = query.Where(r => r.CustomerAccountId == request.CustomerId.Value);

            if (request.ProductId.HasValue)
                query = query.Where(r => r.ProductId == request.ProductId.Value);

            if (!string.IsNullOrWhiteSpace(request.Search))
                query = query.Where(r =>
                    r.CustomerAccount.BusinessName.ToLower().Contains(request.Search.ToLower()) ||
                    (r.ProductId != null && r.Product!.Name.ToLower().Contains(request.Search.ToLower())));

            query = query
                .OrderBy(r => r.CustomerAccount.BusinessName)
                .ThenBy(r => r.ProductId == null ? 0 : 1)
                .ThenBy(r => r.Product!.Name);

            var result = await new QueryBuilder<CustomerMarkupRule>(query)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(r => (object)new CustomerMarkupRuleResponse
                {
                    Id = r.Id,
                    CustomerId = r.CustomerAccountId,
                    CustomerName = r.CustomerAccount.BusinessName,
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

public class GetAllCustomerMarkupListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/customers/markups",
            async (
                ISender sender,
                [FromQuery] string? search,
                [FromQuery] string? sort,
                [FromQuery] int? pageNumber,
                [FromQuery] int? pageSize,
                [FromQuery] Guid? customerId,
                [FromQuery] Guid? productId) =>
            {
                var result = await sender.Send(new GetAllCustomerMarkupList.Query
                {
                    Search = search,
                    Sort = sort,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    CustomerId = customerId,
                    ProductId = productId
                });
                return result.IsFailure
                    ? Results.BadRequest(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("List all customer markup rules")
        .WithDescription(
            "Returns paginated markup rules across all customers. " +
            "Filter by customerId to scope to one customer, or by productId to see all customers with a rule for a specific product. " +
            "Search matches against customer name or product name. " +
            "Results are ordered by customer name, then customer-wide rules first, then by product name.")
        .Produces<Paginator.PaginatedData<SetCustomerMarkup.CustomerMarkupRuleResponse>>(200)
        .RequireAuthorization();
    }
}
