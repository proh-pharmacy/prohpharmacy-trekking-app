using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Staff.CreateStaff;

namespace prohpharmacy_trekking_app.Features.Staff;

public static class GetStaff
{
    public class Query : IRequest<Result<StaffResponse>>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<StaffResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<StaffResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var staff = await _db.StaffMembers
                .Include(s => s.Branch)
                .Include(s => s.ApplicationUser)
                .Include(s => s.DeviceAssignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.Device)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

            if (staff is null)
                return Result.Failure<StaffResponse>(Error.CreateNotFoundError("Staff member not found."));

            var activeDevice = staff.DeviceAssignments.FirstOrDefault();

            return Result.Success(
                CreateStaff.Handler.ToResponse(staff, staff.Branch?.Name ?? string.Empty,
                    staff.ApplicationUser is not null,
                    currentDeviceId: activeDevice?.DeviceId,
                    currentDeviceName: activeDevice?.Device?.Name));
        }
    }
}

public class GetStaffEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/staff/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetStaff.Query { Id = id });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Staff")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Staff)
        .WithSummary("Get a staff member by ID")
        .Produces<StaffResponse>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
