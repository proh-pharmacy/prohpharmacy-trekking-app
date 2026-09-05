using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Drivers;

public static class SyncDriversToTraccar
{
    public class Command : IRequest<Result<SyncResponse>>
    {
        public bool Force { get; set; }
    }

    public class SyncResponse
    {
        public int Synced { get; set; }
        public int Failed { get; set; }
        public int AlreadySynced { get; set; }
        public int Deleted { get; set; }
        public List<string> Errors { get; set; } = [];
    }

    internal sealed class Handler : IRequestHandler<Command, Result<SyncResponse>>
    {
        private readonly AppDbContext _db;
        private readonly ITraccarService _traccar;

        public Handler(AppDbContext db, ITraccarService traccar)
        {
            _db = db;
            _traccar = traccar;
        }

        public async Task<Result<SyncResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var staff = await _db.StaffMembers
                .Include(s => s.Branch)
                .ToListAsync(cancellationToken);

            var response = new SyncResponse();

            List<int> traccarIdsBefore = [];
            if (request.Force)
            {
                var allTraccar = await _traccar.GetAllDriversAsync(cancellationToken);
                traccarIdsBefore = allTraccar.Select(d => d.Id).ToList();

                foreach (var s in staff)
                {
                    s.TraccarDriverId = null;
                    s.UpdatedAt = DateTime.UtcNow;
                }
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                response.AlreadySynced = staff.Count(s => s.TraccarDriverId is not null);
            }

            var unsynced = staff.Where(s => s.TraccarDriverId is null).ToList();
            var syncedTraccarIds = new HashSet<int>();

            foreach (var member in unsynced)
            {
                var attributes = new Dictionary<string, string>
                {
                    ["phone"] = member.PhoneNumber,
                    ["branch"] = member.Branch?.Name ?? string.Empty,
                    ["role"] = member.Role ?? string.Empty
                };
                if (!string.IsNullOrEmpty(member.EmployeeNumber))
                    attributes["employeeNumber"] = member.EmployeeNumber;

                var traccarDriver = await _traccar.CreateDriverAsync(
                    member.FullName,
                    Guid.NewGuid().ToString("N"),
                    attributes,
                    cancellationToken);

                if (traccarDriver is not null)
                {
                    member.TraccarDriverId = traccarDriver.Id;
                    member.UpdatedAt = DateTime.UtcNow;
                    syncedTraccarIds.Add(traccarDriver.Id);
                    response.Synced++;
                }
                else
                {
                    response.Failed++;
                    response.Errors.Add($"Failed to sync driver '{member.FullName}' (id: {member.Id}).");
                }
            }

            if (response.Synced > 0)
                await _db.SaveChangesAsync(cancellationToken);

            if (request.Force)
            {
                foreach (var orphanId in traccarIdsBefore.Where(id => !syncedTraccarIds.Contains(id)))
                {
                    var deleted = await _traccar.DeleteDriverAsync(orphanId, cancellationToken);
                    if (deleted)
                        response.Deleted++;
                    else
                        response.Errors.Add($"Failed to delete orphan Traccar driver (traccarId: {orphanId}).");
                }
            }

            return Result.Success(response);
        }
    }
}

public class SyncDriversToTraccarEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/drivers/sync", async (
            [FromQuery] bool force,
            ISender sender) =>
        {
            var result = await sender.Send(new SyncDriversToTraccar.Command { Force = force });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Sync staff as Traccar drivers")
        .WithDescription(
            "Creates Traccar driver entries for staff members. " +
            "By default only syncs staff missing a Traccar driver ID. " +
            "Use `?force=true` to clear all stored Traccar driver IDs, re-register everyone, and delete any orphan drivers that exist in Traccar but not in the local database — useful after spawning a fresh Traccar instance.")
        .RequireAuthorization();
    }
}
