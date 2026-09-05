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
        public int AlreadySynced { get; set; }
        public int Failed { get; set; }
        public int Deleted { get; set; }
        public List<string> Errors { get; set; } = [];
    }

    internal sealed class Handler : IRequestHandler<Command, Result<SyncResponse>>
    {
        private readonly AppDbContext _db;
        private readonly ITraccarService _traccar;
        private readonly ILogger<Handler> _logger;

        public Handler(AppDbContext db, ITraccarService traccar, ILogger<Handler> logger)
        {
            _db = db;
            _traccar = traccar;
            _logger = logger;
        }

        public async Task<Result<SyncResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var staff = await _db.StaffMembers
                .Include(s => s.Branch)
                .ToListAsync(cancellationToken);

            var response = new SyncResponse();
            var dbChanged = false;

            if (request.Force)
            {
                // Full reconciliation:
                // 1. Fetch all Traccar drivers and build a set of known IDs
                // 2. For staff whose stored TraccarDriverId still exists in Traccar → keep, count as AlreadySynced
                // 3. For staff with no driver ID or whose ID no longer exists in Traccar → create new
                // 4. Delete any Traccar driver whose ID doesn't match any staff member

                var allTraccar = await _traccar.GetAllDriversAsync(cancellationToken);
                var traccarExistingIds = allTraccar.Select(d => d.Id).ToHashSet();
                var knownTraccarIds = new HashSet<int>();

                foreach (var member in staff)
                {
                    var stillExists = member.TraccarDriverId.HasValue &&
                                     traccarExistingIds.Contains(member.TraccarDriverId.Value);

                    if (stillExists)
                    {
                        knownTraccarIds.Add(member.TraccarDriverId!.Value);
                        response.AlreadySynced++;
                    }
                    else
                    {
                        // Driver is missing from Traccar — create it
                        var attributes = BuildAttributes(member);
                        var traccarDriver = await _traccar.CreateDriverAsync(
                            member.FullName,
                            Guid.NewGuid().ToString("N"),
                            attributes,
                            cancellationToken);

                        if (traccarDriver is not null)
                        {
                            member.TraccarDriverId = traccarDriver.Id;
                            member.UpdatedAt = DateTime.UtcNow;
                            knownTraccarIds.Add(traccarDriver.Id);
                            dbChanged = true;
                            response.Synced++;
                        }
                        else
                        {
                            response.Failed++;
                            response.Errors.Add($"Failed to sync driver '{member.FullName}' (id: {member.Id}).");
                        }
                    }
                }

                // Delete orphans — Traccar drivers with no matching staff member
                foreach (var orphan in allTraccar.Where(d => !knownTraccarIds.Contains(d.Id)))
                {
                    var deleted = await _traccar.DeleteDriverAsync(orphan.Id, cancellationToken);
                    if (deleted)
                    {
                        response.Deleted++;
                        _logger.LogInformation("Deleted orphan Traccar driver #{TraccarId} ({Name})", orphan.Id, orphan.Name);
                    }
                    else
                    {
                        response.Errors.Add($"Failed to delete orphan Traccar driver #{orphan.Id} ({orphan.Name}).");
                    }
                }
            }
            else
            {
                // Light sync: only create drivers for staff that have none
                response.AlreadySynced = staff.Count(s => s.TraccarDriverId is not null);
                var unsynced = staff.Where(s => s.TraccarDriverId is null).ToList();

                foreach (var member in unsynced)
                {
                    var traccarDriver = await _traccar.CreateDriverAsync(
                        member.FullName,
                        Guid.NewGuid().ToString("N"),
                        BuildAttributes(member),
                        cancellationToken);

                    if (traccarDriver is not null)
                    {
                        member.TraccarDriverId = traccarDriver.Id;
                        member.UpdatedAt = DateTime.UtcNow;
                        dbChanged = true;
                        response.Synced++;
                    }
                    else
                    {
                        response.Failed++;
                        response.Errors.Add($"Failed to sync driver '{member.FullName}' (id: {member.Id}).");
                    }
                }
            }

            if (dbChanged)
                await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Driver sync complete — synced: {Synced}, already synced: {AlreadySynced}, failed: {Failed}, deleted: {Deleted}",
                response.Synced, response.AlreadySynced, response.Failed, response.Deleted);

            foreach (var err in response.Errors)
                _logger.LogWarning("Driver sync error: {Error}", err);

            return Result.Success(response);
        }

        private static Dictionary<string, string> BuildAttributes(Features.Staff.Entities.StaffMember member)
        {
            var attrs = new Dictionary<string, string>
            {
                ["phone"] = member.PhoneNumber,
                ["branch"] = member.Branch?.Name ?? string.Empty,
                ["role"] = member.Role ?? string.Empty
            };
            if (!string.IsNullOrEmpty(member.EmployeeNumber))
                attrs["employeeNumber"] = member.EmployeeNumber;
            return attrs;
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
            "Default (`force=false`): creates drivers only for staff missing a Traccar driver ID. " +
            "With `force=true`: performs a full reconciliation — keeps drivers that still exist in Traccar, " +
            "re-creates any that are missing, and deletes Traccar drivers with no matching staff member.")
        .RequireAuthorization();
    }
}
