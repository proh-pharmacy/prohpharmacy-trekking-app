using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class DeleteStopReturn
{
    public class Command : IRequest<Result<Unit>>
    {
        public Guid TrekId { get; set; }
        public Guid StopId { get; set; }
        public Guid ReturnId { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<Unit>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var ret = await _db.TrekkingTripStopReturns
                .Include(r => r.TrekkingTripStop)
                    .ThenInclude(s => s.TrekkingTrip)
                .FirstOrDefaultAsync(r => r.Id == request.ReturnId
                    && r.TrekkingTripStopId == request.StopId
                    && r.TrekkingTripStop.TrekkingTripId == request.TrekId, cancellationToken);

            if (ret is null)
                return Result.Failure<Unit>(Error.CreateNotFoundError("Return record not found."));

            var status = ret.TrekkingTripStop.TrekkingTrip.Status;
            if (status == TrekStatus.Completed || status == TrekStatus.Cancelled)
                return Result.Failure<Unit>(Error.BadRequest($"Cannot void a return on a {status} trek."));

            _db.TrekkingTripStopReturns.Remove(ret);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(Unit.Value);
        }
    }
}

public class DeleteStopReturnEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/treks/{trekId:guid}/stops/{stopId:guid}/returns/{returnId:guid}",
            async (Guid trekId, Guid stopId, Guid returnId, ISender sender) =>
            {
                var result = await sender.Send(new DeleteStopReturn.Command
                {
                    TrekId = trekId,
                    StopId = stopId,
                    ReturnId = returnId
                });
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.NoContent();
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Void a product return (admin)")
        .Produces(204)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
