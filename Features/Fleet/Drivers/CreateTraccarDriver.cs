using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Staff.Enums;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Drivers;

public static class CreateTraccarDriver
{
    public class Command : IRequest<Result<DriverResponse>>
    {
        public Guid StaffMemberId { get; set; }
    }

    public class DriverResponse
    {
        public int TraccarDriverId { get; set; }
        public Guid StaffMemberId { get; set; }
        public string StaffName { get; set; } = string.Empty;
        public string TraccarUniqueId { get; set; } = string.Empty;
        public Dictionary<string, string> Attributes { get; set; } = [];
    }

    internal sealed class Handler : IRequestHandler<Command, Result<DriverResponse>>
    {
        private readonly AppDbContext _db;
        private readonly ITraccarService _traccar;

        public Handler(AppDbContext db, ITraccarService traccar)
        {
            _db = db;
            _traccar = traccar;
        }

        public async Task<Result<DriverResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var staff = await _db.StaffMembers
                .Include(s => s.Branch)
                .FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);

            if (staff is null)
                return Result.Failure<DriverResponse>(Error.CreateNotFoundError("Staff member not found."));

            if (staff.EmploymentStatus != EmploymentStatus.Active)
                return Result.Failure<DriverResponse>(
                    Error.BadRequest("Can only register a Traccar driver for an Active staff member."));

            if (staff.TraccarDriverId is not null)
                return Result.Failure<DriverResponse>(
                    Error.Conflict("Staff member already has a Traccar driver registered."));

            var uniqueId = Guid.NewGuid().ToString("N");

            var attributes = new Dictionary<string, string>
            {
                ["phone"] = staff.PhoneNumber,
                ["branch"] = staff.Branch?.Name ?? string.Empty,
                ["role"] = staff.Role ?? string.Empty
            };

            if (!string.IsNullOrEmpty(staff.EmployeeNumber))
                attributes["employeeNumber"] = staff.EmployeeNumber;

            var traccarDriver = await _traccar.CreateDriverAsync(staff.FullName, uniqueId, attributes, cancellationToken);
            if (traccarDriver is null)
                return Result.Failure<DriverResponse>(
                    Error.BadRequest("Failed to register driver in Traccar. Check Traccar configuration."));

            staff.TraccarDriverId = traccarDriver.Id;
            staff.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new DriverResponse
            {
                TraccarDriverId = traccarDriver.Id,
                StaffMemberId = staff.Id,
                StaffName = staff.FullName,
                TraccarUniqueId = traccarDriver.UniqueId,
                Attributes = traccarDriver.Attributes
            });
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
                : Results.Created($"api/v1/fleet/drivers/{result.Value.TraccarDriverId}", result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Register a staff member as a Traccar driver")
        .WithDescription(
            "Creates a driver entry in Traccar for the given staff member. " +
            "Staff attributes (phone, branch, role, employee number) are pushed as Traccar driver attributes. " +
            "The driver is linked to the staff member — only one Traccar driver per staff member is allowed.")
        .Produces<CreateTraccarDriver.DriverResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
