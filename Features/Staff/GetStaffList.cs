using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Features.Staff.CreateStaff;

namespace prohpharmacy_trekking_app.Features.Staff;

public static class GetStaffList
{
    public class Query : IRequest<Result<object>>
    {
        public Guid? BranchId { get; set; }
        public string? Status { get; set; }
        public bool? HasAppAccess { get; set; }
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<object>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = _db.StaffMembers
                .Include(s => s.Branch)
                .Include(s => s.ApplicationUser)
                .AsNoTracking();

            if (request.BranchId.HasValue)
                query = query.Where(s => s.BranchId == request.BranchId.Value);

            if (!string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<prohpharmacy_trekking_app.Features.Staff.Enums.EmploymentStatus>(request.Status, true, out var parsedStatus))
                query = query.Where(s => s.EmploymentStatus == parsedStatus);

            if (request.HasAppAccess.HasValue)
                query = request.HasAppAccess.Value
                    ? query.Where(s => s.ApplicationUser != null)
                    : query.Where(s => s.ApplicationUser == null);

            var devicesByStaff = await _db.TrackingDevices
                .Where(d => d.StaffMemberId != null)
                .AsNoTracking()
                .ToDictionaryAsync(d => d.StaffMemberId!.Value, cancellationToken);

            var result = await new QueryBuilder<Entities.StaffMember>(query)
                .WithSearch(request.Search, nameof(Entities.StaffMember.FirstName),
                    nameof(Entities.StaffMember.LastName), nameof(Entities.StaffMember.EmailAddress),
                    nameof(Entities.StaffMember.EmployeeNumber))
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(s =>
                {
                    devicesByStaff.TryGetValue(s.Id, out var device);
                    return (object)CreateStaff.Handler.ToResponse(s, s.Branch?.Name ?? string.Empty,
                        s.ApplicationUser is not null,
                        currentDeviceId: device?.Id,
                        currentDeviceName: device?.Name);
                });

            return Result.Success(result);
        }
    }
}

public class GetStaffListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/staff", async (
            ISender sender,
            [FromQuery] Guid? branchId,
            [FromQuery] string? status,
            [FromQuery] bool? hasAppAccess,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize) =>
        {
            var result = await sender.Send(new GetStaffList.Query
            {
                BranchId = branchId,
                Status = status,
                HasAppAccess = hasAppAccess,
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize
            });

            return result.IsFailure
                ? Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Staff")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Staff)
        .WithSummary("List / search staff members")
        .WithDescription("Filter by branchId, status (Pending | Active | Suspended | Offboarded), or hasAppAccess (true = on the platform, false = no login account yet).")
        .Produces<Paginator.PaginatedData<StaffResponse>>(200)
        .RequireAuthorization();
    }
}
