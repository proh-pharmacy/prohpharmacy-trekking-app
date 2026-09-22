using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class RemoveCustomerMarkup
{
    public class Command : IRequest<Result>
    {
        public Guid CustomerId { get; set; }
        public Guid MarkupId { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var rule = await db.CustomerMarkupRules
                .FirstOrDefaultAsync(r => r.Id == request.MarkupId && r.CustomerAccountId == request.CustomerId, cancellationToken);

            if (rule is null)
                return Result.Failure(Error.CreateNotFoundError("Markup rule not found."));

            db.CustomerMarkupRules.Remove(rule);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public class RemoveCustomerMarkupEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/customers/{customerId:guid}/markups/{markupId:guid}",
            async (Guid customerId, Guid markupId, ISender sender) =>
            {
                var result = await sender.Send(new RemoveCustomerMarkup.Command
                {
                    CustomerId = customerId,
                    MarkupId = markupId
                });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.NoContent();
            })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Remove a markup rule from a customer")
        .Produces(204)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
