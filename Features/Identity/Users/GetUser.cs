using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Users;

public static class GetUser
{
    public class Query : IRequest<Result<UserDetailResponse>>
    {
        public Guid UserId { get; set; }
    }

    public class UserDetailResponse
    {
        public Guid UserId { get; set; }
        public Guid StaffMemberId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? EmployeeNumber { get; set; }
        public string? Role { get; set; }
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string EmploymentStatus { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = [];
        public List<string> Permissions { get; set; } = [];
        public bool IsActive { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<UserDetailResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<UserDetailResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var user = await _db.ApplicationUsers
                .Include(u => u.StaffMember).ThenInclude(s => s.Branch)
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user is null)
                return Result.Failure<UserDetailResponse>(Error.CreateNotFoundError("User not found."));

            var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
            var permissions = user.UserRoles
                .SelectMany(ur => ur.Role.RolePermissions)
                .Select(rp => rp.Permission)
                .Distinct()
                .ToList();

            return Result.Success(new UserDetailResponse
            {
                UserId = user.Id,
                StaffMemberId = user.StaffMemberId,
                Email = user.Email,
                FullName = user.StaffMember.FullName,
                EmployeeNumber = user.StaffMember.EmployeeNumber,
                Role = user.StaffMember.Role,
                BranchId = user.StaffMember.BranchId,
                BranchName = user.StaffMember.Branch?.Name ?? string.Empty,
                EmploymentStatus = user.StaffMember.EmploymentStatus.ToString(),
                Roles = roles,
                Permissions = permissions,
                IsActive = user.IsActive,
                LastLoginAt = user.LastLoginAt,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }
    }
}

public class GetUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/users/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetUser.Query { UserId = id });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Get an application user by ID")
        .Produces<GetUser.UserDetailResponse>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
