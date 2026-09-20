using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class DeleteCustomerLocation
{
    public class Command : IRequest<Result>
    {
        public Guid CustomerId { get; set; }
        public Guid LocationId { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var location = await db.CustomerLocations
                .FirstOrDefaultAsync(l => l.Id == request.LocationId && l.CustomerAccountId == request.CustomerId, cancellationToken);

            if (location is null)
                return Result.Failure(Error.CreateNotFoundError("Location not found for this customer."));

            db.CustomerLocations.Remove(location);

            var account = await db.CustomerAccounts.FindAsync([request.CustomerId], cancellationToken);
            if (account is not null)
                account.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public class DeleteCustomerLocationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/customers/{customerId:guid}/locations/{locationId:guid}",
            async (Guid customerId, Guid locationId, ISender sender) =>
            {
                var result = await sender.Send(new DeleteCustomerLocation.Command
                {
                    CustomerId = customerId,
                    LocationId = locationId
                });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.NoContent();
            })
            .WithTags("Customers")
            .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
            .WithSummary("Delete a customer location")
            .WithDescription("Permanently removes a location record from a customer. Any location including the primary can be deleted.")
            .Produces(204)
            .Produces<Error>(404)
            .RequireAuthorization();
    }
}
