using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Identity.Users.GetUser;

namespace prohpharmacy_trekking_app.Features.Identity.Auth;

public static class GetMe
{
    public class Query : IRequest<Result<UserDetailResponse>> { }

    internal sealed class Handler : IRequestHandler<Query, Result<UserDetailResponse>>
    {
        private readonly AppDbContext _db;
        private readonly AuthProvider _auth;

        public Handler(AppDbContext db, AuthProvider auth)
        {
            _db = db;
            _auth = auth;
        }

        public async Task<Result<UserDetailResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var rawId = _auth.GetUserId();
            if (rawId is null || !Guid.TryParse(rawId, out var userId))
                return Result.Failure<UserDetailResponse>(Error.Forbidden("Not authenticated."));

            var user = await _db.ApplicationUsers
                .Include(u => u.StaffMember)
                    .ThenInclude(s => s.Branch)
                .Include(u => u.StaffMember)
                    .ThenInclude(s => s.DeviceAssignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.Device)
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user is null)
                return Result.Failure<UserDetailResponse>(Error.CreateNotFoundError("User not found."));

            var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
            var permissions = user.UserRoles
                .SelectMany(ur => ur.Role.RolePermissions)
                .Select(rp => rp.Permission)
                .Distinct()
                .ToList();

            var activeDevice = user.StaffMember.DeviceAssignments.FirstOrDefault();

            return Result.Success(new UserDetailResponse
            {
                UserId = user.Id,
                StaffMemberId = user.StaffMemberId,
                EmployeeNumber = user.StaffMember.EmployeeNumber,
                FirstName = user.StaffMember.FirstName,
                LastName = user.StaffMember.LastName,
                FullName = user.StaffMember.FullName,
                EmailAddress = user.StaffMember.EmailAddress,
                PhoneNumber = user.StaffMember.PhoneNumber,
                Role = user.StaffMember.Role,
                BranchId = user.StaffMember.BranchId,
                BranchName = user.StaffMember.Branch?.Name ?? string.Empty,
                EmploymentStatus = user.StaffMember.EmploymentStatus.ToString(),
                JoinedOn = user.StaffMember.JoinedOn,
                HasAppAccess = true,
                IsActive = user.IsActive,
                SystemRoles = roles,
                Permissions = permissions,
                ProfilePhotoUrl = user.StaffMember.ProfilePhotoObjectKey,
                CurrentDeviceId = activeDevice?.DeviceId,
                CurrentDeviceName = activeDevice?.Device?.Name,
                LastLoginAt = user.LastLoginAt,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }
    }
}

public class GetMeEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/auth/me", async (ISender sender) =>
        {
            var result = await sender.Send(new GetMe.Query());
            return result.IsFailure
                ? Results.Forbid()
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Get the currently authenticated user")
        .WithDescription("Returns the logged-in user's full profile, roles, and permissions derived from their JWT.")
        .Produces<UserDetailResponse>(200)
        .RequireAuthorization();
    }
}
