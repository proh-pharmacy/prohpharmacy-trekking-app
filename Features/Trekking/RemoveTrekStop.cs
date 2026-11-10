using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class RemoveTrekStop
{
    public class Command : IRequest<Result<object>>
    {
        public Guid TrekId { get; set; }
        public Guid StopId { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<object>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<object>> Handle(Command request, CancellationToken cancellationToken)
        {
            var stop = await _db.TrekkingTripStops
                .Include(s => s.TrekkingTrip)
                .FirstOrDefaultAsync(s => s.Id == request.StopId && s.TrekkingTripId == request.TrekId, cancellationToken);

            if (stop is null)
                return Result.Failure<object>(Error.CreateNotFoundError("Stop not found on the specified trek."));

            if (stop.TrekkingTrip.Status != TrekStatus.Draft)
                return Result.Failure<object>(Error.BadRequest(
                    "A customer stop can only be removed while the trek is in Draft. Scheduled stops already have issued invoices."));

            _db.TrekkingTripStops.Remove(stop);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success<object>(new { });
        }
    }
}

public class RemoveTrekStopEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/treks/{trekId:guid}/stops/{stopId:guid}", async (Guid trekId, Guid stopId, ISender sender) =>
        {
            var result = await sender.Send(new RemoveTrekStop.Command { TrekId = trekId, StopId = stopId });
            return result.IsFailure
                ? result.Error.Code == "404" ? Results.NotFound(result.Error) : Results.UnprocessableEntity(result.Error)
                : Results.NoContent();
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Remove a stop from a trekking trip")
        .Produces(204)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
