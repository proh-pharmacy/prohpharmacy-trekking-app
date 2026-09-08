using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Roles;

public static class CreateRole
{
    public class Command : IRequest<Result<GetRoleList.RoleResponse>>
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<string> Permissions { get; set; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
            RuleFor(x => x.Description).MaximumLength(200).When(x => x.Description is not null);
            RuleForEach(x => x.Permissions)
                .Must(p => Identity.Permissions.All.Contains(p))
                .WithMessage("'{PropertyValue}' is not a valid permission.");
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator)
        : IRequestHandler<Command, Result<GetRoleList.RoleResponse>>
    {
        public async Task<Result<GetRoleList.RoleResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<GetRoleList.RoleResponse>(Error.ValidationError(validation));

            var nameTaken = await db.Roles
                .AnyAsync(r => r.Name.ToLower() == request.Name.Trim().ToLower(), cancellationToken);
            if (nameTaken)
                return Result.Failure<GetRoleList.RoleResponse>(Error.Conflict("A role with this name already exists."));

            var permissions = request.Permissions.Distinct().ToList();

            var role = new Role
            {
                Name = request.Name.Trim(),
                Description = request.Description?.Trim() ?? string.Empty,
                IsSystem = false,
                RolePermissions = permissions.Select(p => new RolePermission { Permission = p }).ToList()
            };

            db.Roles.Add(role);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new GetRoleList.RoleResponse
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description,
                Permissions = permissions.OrderBy(p => p).ToList(),
                IsSystem = false
            });
        }
    }
}

public class CreateRoleEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/roles", async (CreateRole.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/roles/{result.Value.Id}", result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Create a new role")
        .WithDescription("Creates a custom role with an optional initial set of permissions. Use `PUT /api/v1/roles/{id}/permissions` to update permissions later.")
        .Produces<GetRoleList.RoleResponse>(201)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
