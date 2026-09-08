using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Roles;

public static class GetRolePermissions
{
    public class Query : IRequest<Result<RolePermissionsResponse>>
    {
        public Guid RoleId { get; set; }
    }

    public class RolePermissionsResponse
    {
        public Guid RoleId { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<PermissionGroup> Groups { get; set; } = [];
    }

    public class PermissionGroup
    {
        public string Module { get; set; } = string.Empty;
        public List<PermissionItem> Permissions { get; set; } = [];
    }

    public class PermissionItem
    {
        public string Key { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<RolePermissionsResponse>>
    {
        public async Task<Result<RolePermissionsResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var role = await db.Roles
                .Include(r => r.RolePermissions)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);

            if (role is null)
                return Result.Failure<RolePermissionsResponse>(Error.CreateNotFoundError("Role not found."));

            var enabledPermissions = role.RolePermissions.Select(rp => rp.Permission).ToHashSet();

            var groups = Identity.Permissions.All
                .GroupBy(p => p.Split('.')[0])
                .Select(g => new PermissionGroup
                {
                    Module = g.Key,
                    Permissions = g.Select(p => new PermissionItem
                    {
                        Key = p,
                        Enabled = enabledPermissions.Contains(p)
                    }).ToList()
                })
                .OrderBy(g => g.Module)
                .ToList();

            return Result.Success(new RolePermissionsResponse
            {
                RoleId = role.Id,
                RoleName = role.Name,
                Description = role.Description,
                Groups = groups
            });
        }
    }
}

public class GetRolePermissionsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/roles/{id:guid}/permissions",
            async (Guid id, ISender sender) =>
            {
                var result = await sender.Send(new GetRolePermissions.Query { RoleId = id });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
            .WithTags("Auth")
            .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
            .WithSummary("Get all permissions for a role")
            .WithDescription("Returns every available permission grouped by module, with Enabled=true/false indicating whether the role currently has it.")
            .Produces<GetRolePermissions.RolePermissionsResponse>(200)
            .Produces<Error>(404)
            .RequireAuthorization();
    }
}
