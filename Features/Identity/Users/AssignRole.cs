using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Users;

public static class AssignRole
{
    public class Command : IRequest<Result<RoleAssignmentResponse>>
    {
        public Guid UserId { get; set; }
        public string RoleName { get; set; } = string.Empty;
    }

    public class RoleAssignmentResponse
    {
        public Guid UserId { get; set; }
        public List<string> Roles { get; set; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.RoleName).NotEmpty().MaximumLength(60);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<RoleAssignmentResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;
        private readonly AuthProvider _auth;

        public Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth)
        {
            _db = db;
            _validator = validator;
            _auth = auth;
        }

        public async Task<Result<RoleAssignmentResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<RoleAssignmentResponse>(Error.ValidationError(validation));

            var user = await _db.ApplicationUsers
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user is null)
                return Result.Failure<RoleAssignmentResponse>(Error.CreateNotFoundError("User not found."));

            var role = await _db.Roles
                .FirstOrDefaultAsync(r => r.Name == request.RoleName, cancellationToken);

            if (role is null)
                return Result.Failure<RoleAssignmentResponse>(Error.CreateNotFoundError($"Role '{request.RoleName}' not found."));

            var alreadyAssigned = user.UserRoles.Any(ur => ur.RoleId == role.Id);
            if (alreadyAssigned)
                return Result.Failure<RoleAssignmentResponse>(
                    Error.Conflict($"User already has the '{request.RoleName}' role."));

            var assignerIdStr = _auth.GetUserId();

            _db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
                AssignedAt = DateTime.UtcNow,
                AssignedByUserId = assignerIdStr is not null ? Guid.Parse(assignerIdStr) : null
            });

            await _db.SaveChangesAsync(cancellationToken);

            var allRoles = await _db.UserRoles
                .Include(ur => ur.Role)
                .Where(ur => ur.UserId == user.Id)
                .Select(ur => ur.Role.Name)
                .ToListAsync(cancellationToken);

            return Result.Success(new RoleAssignmentResponse
            {
                UserId = user.Id,
                Roles = allRoles
            });
        }
    }
}

public class AssignRoleEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/users/{id:guid}/roles", async (Guid id, AssignRole.Command command, ISender sender) =>
        {
            command.UserId = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Assign a role to a user")
        .RequireAuthorization();
    }
}
