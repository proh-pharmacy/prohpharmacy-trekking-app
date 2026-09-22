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

public static class GetCustomerMarkupList
{
    public class Query : IRequest<Result<object>>
    {
        public Guid CustomerId { get; set; }
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
            var customer = await db.CustomerAccounts.FindAsync([request.CustomerId], cancellationToken);
            if (customer is null)
                return Result.Failure<object>(Error.CreateNotFoundError("Customer not found."));

            var query = db.CustomerMarkupRules
                .Include(r => r.Product)
                .Where(r => r.CustomerAccountId == request.CustomerId)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(request.Search))
                query = query.Where(r => r.ProductId == null ||
                    r.Product!.Name.ToLower().Contains(request.Search.ToLower()));

            query = query
                .OrderBy(r => r.ProductId == null ? 0 : 1)
                .ThenBy(r => r.Product!.Name);

            var result = await new QueryBuilder<CustomerMarkupRule>(query)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(r => (object)new CustomerMarkupRuleResponse
                {
                    Id = r.Id,
                    CustomerId = customer.Id,
                    CustomerName = customer.BusinessName,
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

public class GetCustomerMarkupListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/customers/{customerId:guid}/markups",
            async (
                Guid customerId,
                ISender sender,
                [FromQuery] string? search,
                [FromQuery] string? sort,
                [FromQuery] int? pageNumber,
                [FromQuery] int? pageSize) =>
            {
                var result = await sender.Send(new GetCustomerMarkupList.Query
                {
                    CustomerId = customerId,
                    Search = search,
                    Sort = sort,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("List markup rules for a customer")
        .WithDescription(
            "Returns paginated markup rules for the given customer. " +
            "The customer-wide rule (no ProductId) always appears first. " +
            "Search filters by product name.")
        .Produces<Paginator.PaginatedData<SetCustomerMarkup.CustomerMarkupRuleResponse>>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
