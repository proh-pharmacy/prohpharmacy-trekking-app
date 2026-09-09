using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Drivers;

public static class DeleteTraccarDriver
{
    public class Command : IRequest<Result>
    {
        public Guid StaffMemberId { get; set; }
    }

    internal sealed class Handler(AppDbContext db, ITraccarService traccar, ILogger<Handler> logger)
        : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var driver = await db.FleetDrivers
                .FirstOrDefaultAsync(d => d.StaffMemberId == request.StaffMemberId, cancellationToken);

            if (driver is null)
                return Result.Failure(Error.CreateNotFoundError("Fleet driver not found."));

            if (driver.TraccarDriverId.HasValue)
            {
                var deleted = await traccar.DeleteDriverAsync(driver.TraccarDriverId.Value, cancellationToken);
                if (!deleted)
                    logger.LogWarning("Failed to delete Traccar driver #{TraccarId} — removing locally anyway", driver.TraccarDriverId.Value);
            }

            db.FleetDrivers.Remove(driver);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public class DeleteTraccarDriverEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/fleet/drivers/{staffMemberId:guid}", async (Guid staffMemberId, ISender sender) =>
        {
            var result = await sender.Send(new DeleteTraccarDriver.Command { StaffMemberId = staffMemberId });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.NoContent();
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Remove a fleet driver")
        .WithDescription("Removes the driver from the local fleet list and deletes from Traccar if already synced.")
        .Produces(204)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
