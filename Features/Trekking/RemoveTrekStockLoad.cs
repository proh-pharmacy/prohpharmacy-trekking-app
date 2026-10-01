using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class RemoveTrekStockLoad
{
    public class Command : IRequest<Result<bool>>
    {
        public Guid TrekId { get; set; }
        public Guid ProductId { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Command, Result<bool>>
    {
        public async Task<Result<bool>> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<bool>(Error.CreateNotFoundError("Trek not found."));

            if (trip.Status == TrekStatus.Completed || trip.Status == TrekStatus.Cancelled)
                return Result.Failure<bool>(Error.BadRequest($"Cannot modify stock loads on a {trip.Status} trek."));

            var load = await db.TrekStockLoads
                .FirstOrDefaultAsync(l => l.TrekkingTripId == request.TrekId && l.ProductId == request.ProductId, cancellationToken);

            if (load is null)
                return Result.Failure<bool>(Error.CreateNotFoundError("Stock load entry not found for this product."));

            db.TrekStockLoads.Remove(load);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(true);
        }
    }
}

public class RemoveTrekStockLoadEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/treks/{trekId:guid}/stock-loads/{productId:guid}",
            async (Guid trekId, Guid productId, ISender sender) =>
            {
                var result = await sender.Send(new RemoveTrekStockLoad.Command { TrekId = trekId, ProductId = productId });
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.NoContent();
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Remove a product from the trek stock load")
        .Produces(204)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
