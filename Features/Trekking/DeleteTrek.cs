using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class DeleteTrek
{
    public class Command : IRequest<Result>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

            if (trip is null)
                return Result.Failure(Error.CreateNotFoundError("Trekking trip not found."));

            if (trip.Status != TrekStatus.Draft && trip.Status != TrekStatus.Scheduled)
                return Result.Failure(Error.BadRequest($"Only Draft or Scheduled treks can be deleted. This trek is {trip.Status}."));

            db.TrekkingTrips.Remove(trip);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public class DeleteTrekEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/treks/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new DeleteTrek.Command { Id = id });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.NoContent();
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Delete a trekking trip")
        .WithDescription("Permanently deletes a trek and all its stops, products, and returns. Only treks in Draft or Scheduled status can be deleted. Cascades to all child records.")
        .Produces(204)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
