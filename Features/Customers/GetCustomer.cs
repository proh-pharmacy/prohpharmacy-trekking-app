using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class GetCustomer
{
    public class Query : IRequest<Result<CreateCustomer.CustomerResponse>>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<CreateCustomer.CustomerResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<CreateCustomer.CustomerResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var account = await _db.CustomerAccounts
                .Include(a => a.Region)
                .Include(a => a.OwningBranch)
                .Include(a => a.RegisteredBy)
                .Include(a => a.People.Where(p => p.IsPrimaryContact && p.IsActive))
                .Include(a => a.Locations.Where(l => l.IsPrimary))
                    .ThenInclude(l => l.District)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

            if (account is null)
                return Result.Failure<CreateCustomer.CustomerResponse>(Error.CreateNotFoundError("Customer not found."));

            var primaryPerson = account.People.FirstOrDefault();
            var primaryLocation = account.Locations.FirstOrDefault();

            return Result.Success(CreateCustomer.Handler.ToResponse(
                account,
                account.Region,
                account.OwningBranch,
                account.RegisteredBy,
                primaryPerson,
                primaryLocation,
                primaryLocation?.District));
        }
    }
}

public class GetCustomerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/customers/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetCustomer.Query { Id = id });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Get a customer by ID")
        .RequireAuthorization();
    }
}
