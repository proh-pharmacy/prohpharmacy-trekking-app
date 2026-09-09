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

    internal sealed class Handler(AppDbContext db, ITraccarService traccar, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<SyncResponse>>
    {
        public async Task<Result<SyncResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var drivers = await db.FleetDrivers
                .Include(d => d.StaffMember)
                    .ThenInclude(s => s.Branch)
                .ToListAsync(cancellationToken);

            var response = new SyncResponse();
            var dbChanged = false;

            var allTraccar = await traccar.GetAllDriversAsync(cancellationToken);
            var traccarExistingIds = allTraccar.Select(d => d.Id).ToHashSet();
            var knownTraccarIds = new HashSet<int>();

            foreach (var driver in drivers)
            {
                var stillExists = driver.TraccarDriverId.HasValue &&
                                  traccarExistingIds.Contains(driver.TraccarDriverId.Value);

                if (stillExists)
                {
                    knownTraccarIds.Add(driver.TraccarDriverId!.Value);
                    response.AlreadySynced++;
                }
                else
                {
                    var attributes = BuildAttributes(driver.StaffMember);
                    var traccarDriver = await traccar.CreateDriverAsync(
                        driver.StaffMember.FullName,
                        Guid.NewGuid().ToString("N"),
                        attributes,
                        cancellationToken);

                    if (traccarDriver is not null)
                    {
                        driver.TraccarDriverId = traccarDriver.Id;
                        driver.TraccarUniqueId = traccarDriver.UniqueId;
                        driver.UpdatedAt = DateTime.UtcNow;
                        knownTraccarIds.Add(traccarDriver.Id);
                        dbChanged = true;
                        response.Synced++;
                    }
                    else
                    {
                        response.Failed++;
                        response.Errors.Add($"Failed to sync driver '{driver.StaffMember.FullName}' (staffId: {driver.StaffMemberId}).");
                    }
                }
            }

            if (request.Force)
            {
                foreach (var orphan in allTraccar.Where(d => !knownTraccarIds.Contains(d.Id)))
                {
                    var deleted = await traccar.DeleteDriverAsync(orphan.Id, cancellationToken);
                    if (deleted)
                    {
                        response.Deleted++;
                        logger.LogInformation("Deleted orphan Traccar driver #{TraccarId} ({Name})", orphan.Id, orphan.Name);
                    }
                    else
                    {
                        response.Errors.Add($"Failed to delete orphan Traccar driver #{orphan.Id} ({orphan.Name}).");
                    }
                }
            }

            if (dbChanged)
                await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Driver sync complete — synced: {Synced}, already synced: {AlreadySynced}, failed: {Failed}, deleted: {Deleted}",
                response.Synced, response.AlreadySynced, response.Failed, response.Deleted);

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
        .WithSummary("Sync fleet drivers to Traccar")
        .WithDescription(
            "Syncs registered fleet drivers to Traccar. " +
            "Re-creates any driver whose Traccar entry is missing. " +
            "With `force=true`, also deletes Traccar drivers not in the local fleet drivers list.")
        .Produces<SyncDriversToTraccar.SyncResponse>(200)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
