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

    internal sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly AppDbContext _db;
        private readonly ITraccarService _traccar;

        public Handler(AppDbContext db, ITraccarService traccar)
        {
            _db = db;
            _traccar = traccar;
        }

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var staff = await _db.StaffMembers
                .FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);

            if (staff is null)
                return Result.Failure(Error.CreateNotFoundError("Staff member not found."));

            if (staff.TraccarDriverId is null)
                return Result.Failure(Error.CreateNotFoundError("Staff member has no Traccar driver registered."));

            await _traccar.DeleteDriverAsync(staff.TraccarDriverId.Value, cancellationToken);

            staff.TraccarDriverId = null;
            staff.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

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
        .WithSummary("Remove a staff member's Traccar driver registration")
        .WithDescription("Deletes the driver from Traccar and clears the Traccar driver ID from the staff member record.")
        .RequireAuthorization();
    }
}
