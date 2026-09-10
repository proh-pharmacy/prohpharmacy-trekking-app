using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Search;

public static class GlobalSearch
{
    public class Query : IRequest<Result<SearchResponse>>
    {
        public string Q { get; set; } = string.Empty;
    }

    public class SearchResponse
    {
        public string Query { get; set; } = string.Empty;
        public List<CustomerResult> Customers { get; set; } = [];
        public List<ProductResult> Products { get; set; } = [];
        public List<StaffResult> Staff { get; set; } = [];
        public List<TrekResult> Treks { get; set; } = [];
    }

    public class CustomerResult
    {
        public Guid Id { get; set; }
        public string BusinessName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string PrimaryPhoneNumber { get; set; } = string.Empty;
    }

    public class ProductResult
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Unit { get; set; }
    }

    public class StaffResult
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Role { get; set; }
        public string BranchName { get; set; } = string.Empty;
    }

    public class TrekResult
    {
        public Guid Id { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateOnly ScheduledDate { get; set; }
        public string BranchName { get; set; } = string.Empty;
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<SearchResponse>>
    {
        public async Task<Result<SearchResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            if (request.Q.Trim().Length < 2)
                return Result.Failure<SearchResponse>(Error.BadRequest("Search query must be at least 2 characters."));

            var q = request.Q.Trim();
            var pattern = $"%{q}%";

            var customers = await db.CustomerAccounts
                .Where(ca =>
                    EF.Functions.ILike(ca.BusinessName, pattern) ||
                    EF.Functions.ILike(ca.CustomerCode, pattern) ||
                    EF.Functions.ILike(ca.PrimaryPhoneNumber, pattern))
                .AsNoTracking()
                .Take(5)
                .Select(ca => new CustomerResult
                {
                    Id = ca.Id,
                    BusinessName = ca.BusinessName,
                    CustomerCode = ca.CustomerCode,
                    PrimaryPhoneNumber = ca.PrimaryPhoneNumber
                })
                .ToListAsync(cancellationToken);

            var products = await db.Products
                .Where(p => p.IsActive && EF.Functions.ILike(p.Name, pattern))
                .AsNoTracking()
                .Take(5)
                .Select(p => new ProductResult
                {
                    Id = p.Id,
                    Name = p.Name,
                    Unit = p.Unit
                })
                .ToListAsync(cancellationToken);

            var staff = await db.StaffMembers
                .Where(s =>
                    EF.Functions.ILike(s.FirstName + " " + s.LastName, pattern) ||
                    EF.Functions.ILike(s.FirstName, pattern) ||
                    EF.Functions.ILike(s.LastName, pattern) ||
                    (s.EmployeeNumber != null && EF.Functions.ILike(s.EmployeeNumber, pattern)) ||
                    EF.Functions.ILike(s.EmailAddress, pattern))
                .AsNoTracking()
                .Take(5)
                .Select(s => new StaffResult
                {
                    Id = s.Id,
                    FullName = s.FirstName + " " + s.LastName,
                    Role = s.Role,
                    BranchName = s.Branch.Name
                })
                .ToListAsync(cancellationToken);

            var treks = await db.TrekkingTrips
                .Where(t => EF.Functions.ILike(t.TrekNumber, pattern))
                .AsNoTracking()
                .Take(5)
                .Select(t => new TrekResult
                {
                    Id = t.Id,
                    TrekNumber = t.TrekNumber,
                    Status = t.Status.ToString(),
                    ScheduledDate = t.ScheduledDate,
                    BranchName = t.Branch.Name
                })
                .ToListAsync(cancellationToken);

            return Result.Success(new SearchResponse
            {
                Query = q,
                Customers = customers,
                Products = products,
                Staff = staff,
                Treks = treks
            });
        }
    }
}

public class GlobalSearchEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/search", async (
            ISender sender,
            [FromQuery] string? q) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.UnprocessableEntity(Error.BadRequest("Search query is required."));

            var result = await sender.Send(new GlobalSearch.Query { Q = q });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Search")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Global search")
        .WithDescription(
            "Searches across customers, products, staff, and treks in a single request. " +
            "Returns up to 5 results per category. All four arrays are always present — empty if no match. " +
            "Minimum query length is 2 characters. " +
            "Debounce requests on the frontend (300ms recommended) to avoid hammering the DB on every keystroke.")
        .Produces<GlobalSearch.SearchResponse>(200)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
