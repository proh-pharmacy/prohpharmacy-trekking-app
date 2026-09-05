using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Roles;

public static class GetRoleList
{
    public class Query : IRequest<Result<List<RoleResponse>>>
    {
    }

    public class RoleResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = [];
        public bool IsSystem { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<List<RoleResponse>>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<List<RoleResponse>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var roles = await _db.Roles
                .Include(r => r.RolePermissions)
                .OrderBy(r => r.Name)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var response = roles.Select(r => new RoleResponse
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                Permissions = r.RolePermissions.Select(rp => rp.Permission).OrderBy(p => p).ToList(),
                IsSystem = r.IsSystem
            }).ToList();

            return Result.Success(response);
        }
    }
}

public class GetRoleListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/roles", async (ISender sender) =>
        {
            var result = await sender.Send(new GetRoleList.Query());
            return result.IsFailure
                ? Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("List all roles and their permissions")
        .RequireAuthorization();
    }
}
