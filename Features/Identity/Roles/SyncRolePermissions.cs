using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Roles;

public static class SyncRolePermissions
{
    public class Command : IRequest<Result<SyncResponse>>
    {
        public Guid RoleId { get; set; }
        public List<string> Permissions { get; set; } = [];
    }

    public class SyncResponse
    {
        public Guid RoleId { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleForEach(x => x.Permissions)
                .Must(p => Identity.Permissions.All.Contains(p))
                .WithMessage("'{PropertyValue}' is not a valid permission.");
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator)
        : IRequestHandler<Command, Result<SyncResponse>>
    {
        public async Task<Result<SyncResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<SyncResponse>(Error.ValidationError(validation));

            var role = await db.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);

            if (role is null)
                return Result.Failure<SyncResponse>(Error.CreateNotFoundError("Role not found."));

            var incoming = request.Permissions.Distinct().ToHashSet();
            var existing = role.RolePermissions.Select(rp => rp.Permission).ToHashSet();

            var toAdd = incoming.Except(existing).ToList();
            var toRemove = role.RolePermissions.Where(rp => !incoming.Contains(rp.Permission)).ToList();

            db.RolePermissions.RemoveRange(toRemove);
            db.RolePermissions.AddRange(toAdd.Select(p => new RolePermission
            {
                RoleId = role.Id,
                Permission = p
            }));

            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new SyncResponse
            {
                RoleId = role.Id,
                RoleName = role.Name,
                Permissions = incoming.OrderBy(p => p).ToList()
            });
        }
    }
}

public class SyncRolePermissionsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("api/v1/roles/{id:guid}/permissions",
            async (Guid id, SyncRolePermissions.Command command, ISender sender) =>
            {
                command.RoleId = id;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
            .WithTags("Auth")
            .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
            .WithSummary("Sync permissions for a role")
            .WithDescription("Replaces the role's full permission set. Send all enabled permissions — anything not in the list is removed.")
            .Produces<SyncRolePermissions.SyncResponse>(200)
            .Produces<Error>(404)
            .Produces<Error>(422)
            .RequireAuthorization();
    }
}
