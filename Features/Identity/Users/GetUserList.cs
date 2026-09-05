using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;

namespace prohpharmacy_trekking_app.Features.Identity.Users;

public static class GetUserList
{
    public class Query : IRequest<Result<object>>
    {
        public Guid? BranchId { get; set; }
        public string? Role { get; set; }
        public bool? IsActive { get; set; }
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
    }

    public class UserSummaryResponse
    {
        public Guid UserId { get; set; }
        public Guid StaffMemberId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string EmployeeNumber { get; set; } = string.Empty;
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = [];
        public bool IsActive { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<object>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var usersQuery = _db.ApplicationUsers
                .Include(u => u.StaffMember).ThenInclude(s => s.Branch)
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .AsNoTracking();

            if (request.IsActive.HasValue)
                usersQuery = usersQuery.Where(u => u.IsActive == request.IsActive.Value);

            if (request.BranchId.HasValue)
                usersQuery = usersQuery.Where(u => u.StaffMember.BranchId == request.BranchId.Value);

            if (!string.IsNullOrWhiteSpace(request.Role))
                usersQuery = usersQuery.Where(u => u.UserRoles.Any(ur => ur.Role.Name.ToLower() == request.Role.ToLower()));

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var search = request.Search.ToLower();
                usersQuery = usersQuery.Where(u =>
                    u.Email.ToLower().Contains(search) ||
                    u.StaffMember.FirstName.ToLower().Contains(search) ||
                    u.StaffMember.LastName.ToLower().Contains(search) ||
                    u.StaffMember.EmployeeNumber.ToLower().Contains(search));
            }

            var total = await usersQuery.CountAsync(cancellationToken);
            var pageNum = request.PageNumber ?? 1;
            var pageSize = request.PageSize ?? 20;

            var users = await usersQuery
                .OrderByDescending(u => u.CreatedAt)
                .Skip((pageNum - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            var data = users.Select(u => new UserSummaryResponse
            {
                UserId = u.Id,
                StaffMemberId = u.StaffMemberId,
                Email = u.Email,
                FullName = u.StaffMember.FullName,
                EmployeeNumber = u.StaffMember.EmployeeNumber,
                BranchId = u.StaffMember.BranchId,
                BranchName = u.StaffMember.Branch?.Name ?? string.Empty,
                Roles = u.UserRoles.Select(ur => ur.Role.Name).ToList(),
                IsActive = u.IsActive,
                LastLoginAt = u.LastLoginAt,
                CreatedAt = u.CreatedAt
            }).ToList();

            return Result.Success<object>(new
            {
                TotalCount = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                CurrentPage = pageNum,
                PageSize = pageSize,
                Data = data
            });
        }
    }
}

public class GetUserListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/users", async (
            ISender sender,
            [FromQuery] Guid? branchId,
            [FromQuery] string? role,
            [FromQuery] bool? isActive,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize) =>
        {
            var result = await sender.Send(new GetUserList.Query
            {
                BranchId = branchId,
                Role = role,
                IsActive = isActive,
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize
            });

            return result.IsFailure
                ? Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("List application users")
        .WithDescription("Filter by branchId, role name, isActive, or search term.")
        .RequireAuthorization();
    }
}
