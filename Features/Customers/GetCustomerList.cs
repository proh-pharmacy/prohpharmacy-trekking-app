using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class GetCustomerList
{
    public class Query : IRequest<Result<object>>
    {
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public Guid? RegionId { get; set; }
        public Guid? BranchId { get; set; }
        public string? CustomerType { get; set; }
        public string? Status { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<object>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = _db.CustomerAccounts
                .Include(a => a.Region)
                .Include(a => a.OwningBranch)
                .Include(a => a.RegisteredBy)
                .Include(a => a.People.Where(p => p.IsPrimaryContact && p.IsActive))
                .Include(a => a.Locations.Where(l => l.IsPrimary))
                    .ThenInclude(l => l.District)
                .AsNoTracking();

            if (request.RegionId.HasValue)
                query = query.Where(a => a.RegionId == request.RegionId.Value);

            if (request.BranchId.HasValue)
                query = query.Where(a => a.OwningBranchId == request.BranchId.Value);

            if (!string.IsNullOrWhiteSpace(request.CustomerType))
                query = query.Where(a => a.CustomerType.ToString().ToLower() == request.CustomerType.ToLower());

            if (!string.IsNullOrWhiteSpace(request.Status))
                query = query.Where(a => a.RegistrationStatus.ToString().ToLower() == request.Status.ToLower());

            var result = await new QueryBuilder<Features.Customers.Entities.CustomerAccount>(query)
                .WithSearch(request.Search, nameof(Entities.CustomerAccount.BusinessName),
                    nameof(Entities.CustomerAccount.CustomerCode),
                    nameof(Entities.CustomerAccount.PrimaryPhoneNumber))
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(a => (object)CreateCustomer.Handler.ToResponse(
                    a,
                    a.Region,
                    a.OwningBranch,
                    a.RegisteredBy,
                    a.People.FirstOrDefault(),
                    a.Locations.FirstOrDefault(),
                    a.Locations.FirstOrDefault()?.District));

            return Result.Success(result);
        }
    }
}

public class GetCustomerListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/customers", async (
            ISender sender,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromQuery] Guid? regionId,
            [FromQuery] Guid? branchId,
            [FromQuery] string? customerType,
            [FromQuery] string? status) =>
        {
            var result = await sender.Send(new GetCustomerList.Query
            {
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize,
                RegionId = regionId,
                BranchId = branchId,
                CustomerType = customerType,
                Status = status
            });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("List customers")
        .WithDescription("Returns a paginated list of customers. Filter by region, branch, type or status. Search by business name, customer code or phone number.")
        .RequireAuthorization();
    }
}
