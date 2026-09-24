using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class StartTrekByDriverToken
{
    public class Command : IRequest<Result>
    {
        public Guid Token { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            if (trip.Status == TrekStatus.Completed)
                return Result.Failure(Error.BadRequest("This trek is already completed."));

            if (trip.Status == TrekStatus.Cancelled)
                return Result.Failure(Error.BadRequest("This trek has been cancelled."));

            if (trip.Status == TrekStatus.InProgress)
                return Result.Success();

            trip.Status = TrekStatus.InProgress;
            trip.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public class StartTrekByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/start",
            async (Guid token, ISender sender) =>
            {
                var result = await sender.Send(new StartTrekByDriverToken.Command { Token = token });
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.NoContent();
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Start a trek from the driver portal")
        .WithDescription("Sets the trek status to InProgress. Idempotent — safe to call if already in progress.")
        .Produces(204)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
