using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Users;

public static class RemoveRole
{
    public class Command : IRequest<Result<RoleRemovalResponse>>
    {
        public Guid UserId { get; set; }
        public string RoleName { get; set; } = string.Empty;
    }

    public class RoleRemovalResponse
    {
        public Guid UserId { get; set; }
        public List<string> Roles { get; set; } = [];
    }

    internal sealed class Handler : IRequestHandler<Command, Result<RoleRemovalResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<RoleRemovalResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await _db.ApplicationUsers
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user is null)
                return Result.Failure<RoleRemovalResponse>(Error.CreateNotFoundError("User not found."));

            var userRole = user.UserRoles.FirstOrDefault(ur => ur.Role.Name == request.RoleName);
            if (userRole is null)
                return Result.Failure<RoleRemovalResponse>(
                    Error.CreateNotFoundError($"User does not have the '{request.RoleName}' role."));

            _db.UserRoles.Remove(userRole);
            await _db.SaveChangesAsync(cancellationToken);

            var remaining = user.UserRoles
                .Where(ur => ur.RoleId != userRole.RoleId)
                .Select(ur => ur.Role.Name)
                .ToList();

            return Result.Success(new RoleRemovalResponse
            {
                UserId = user.Id,
                Roles = remaining
            });
        }
    }
}

public class RemoveRoleEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/users/{id:guid}/roles/{roleName}", async (Guid id, string roleName, ISender sender) =>
        {
            var result = await sender.Send(new RemoveRole.Command { UserId = id, RoleName = roleName });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Remove a role from a user")
        .Produces<RemoveRole.RoleRemovalResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
