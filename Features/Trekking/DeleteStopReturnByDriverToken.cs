using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class DeleteStopReturnByDriverToken
{
    public class Command : IRequest<Result<Unit>>
    {
        public Guid Token { get; set; }
        public Guid StopId { get; set; }
        public Guid ReturnId { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<Unit>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await _db.TrekkingTrips
                .Include(t => t.Stops.Where(s => s.Id == request.StopId))
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<Unit>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            if (trip.Stops.All(s => s.Id != request.StopId))
                return Result.Failure<Unit>(Error.CreateNotFoundError("Stop not found on this trek."));

            if (trip.Status == TrekStatus.Completed || trip.Status == TrekStatus.Cancelled)
                return Result.Failure<Unit>(Error.BadRequest($"Cannot void a return on a {trip.Status} trek."));

            var ret = await _db.TrekkingTripStopReturns
                .FirstOrDefaultAsync(r => r.Id == request.ReturnId && r.TrekkingTripStopId == request.StopId, cancellationToken);

            if (ret is null)
                return Result.Failure<Unit>(Error.CreateNotFoundError("Return record not found."));

            _db.TrekkingTripStopReturns.Remove(ret);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(Unit.Value);
        }
    }
}

public class DeleteStopReturnByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/treks/driver/{token:guid}/stops/{stopId:guid}/returns/{returnId:guid}",
            async (Guid token, Guid stopId, Guid returnId, ISender sender) =>
            {
                var result = await sender.Send(new DeleteStopReturnByDriverToken.Command
                {
                    Token = token,
                    StopId = stopId,
                    ReturnId = returnId
                });
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.NoContent();
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Void a product return (driver portal)")
        .Produces(204)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
