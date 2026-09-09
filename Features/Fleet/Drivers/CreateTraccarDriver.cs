using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Fleet.Drivers.GetFleetDriverList;

namespace prohpharmacy_trekking_app.Features.Fleet.Drivers;

public static class CreateTraccarDriver
{
    public class Command : IRequest<Result<FleetDriverResponse>>
    {
        public Guid StaffMemberId { get; set; }
    }

    internal sealed class Handler(AppDbContext db, ITraccarService traccar, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<FleetDriverResponse>>
    {
        public async Task<Result<FleetDriverResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var staff = await db.StaffMembers
                .Include(s => s.Branch)
                .FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);

            if (staff is null)
                return Result.Failure<FleetDriverResponse>(Error.CreateNotFoundError("Staff member not found."));

            var alreadyRegistered = await db.FleetDrivers
                .AnyAsync(d => d.StaffMemberId == request.StaffMemberId, cancellationToken);

            if (alreadyRegistered)
                return Result.Failure<FleetDriverResponse>(Error.Conflict("Staff member is already registered as a fleet driver."));

            var driver = new FleetDriver
            {
                StaffMemberId = staff.Id,
                CreatedAt = DateTime.UtcNow
            };

            var attributes = new Dictionary<string, string>
            {
                ["phone"] = staff.PhoneNumber,
                ["branch"] = staff.Branch?.Name ?? string.Empty,
                ["role"] = staff.Role ?? string.Empty
            };
            if (!string.IsNullOrEmpty(staff.EmployeeNumber))
                attributes["employeeNumber"] = staff.EmployeeNumber;

            var traccarDriver = await traccar.CreateDriverAsync(staff.FullName, Guid.NewGuid().ToString("N"), attributes, cancellationToken);
            if (traccarDriver is not null)
            {
                driver.TraccarDriverId = traccarDriver.Id;
                driver.TraccarUniqueId = traccarDriver.UniqueId;
                logger.LogInformation("Fleet driver {StaffId} ({Name}) registered in Traccar as #{TraccarId}",
                    staff.Id, staff.FullName, traccarDriver.Id);
            }
            else
            {
                logger.LogWarning("Fleet driver {StaffId} ({Name}) created locally but Traccar registration failed — sync manually",
                    staff.Id, staff.FullName);
            }

            db.FleetDrivers.Add(driver);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(GetFleetDriverList.Handler.ToResponse(driver, staff));
        }
    }
}

public class CreateTraccarDriverEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/drivers", async (CreateTraccarDriver.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/fleet/drivers/{result.Value.StaffMemberId}", result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Register a staff member as a fleet driver")
        .WithDescription("Registers the staff member as a fleet driver and immediately syncs to Traccar. If Traccar is unreachable, the driver is saved locally and can be synced manually.")
        .Produces<FleetDriverResponse>(201)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
