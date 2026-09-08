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
        public List<string> RoleNames { get; set; } = [];
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
            RuleFor(x => x.RoleNames).NotEmpty().WithMessage("At least one role is required.");
            RuleForEach(x => x.RoleNames).NotEmpty().MaximumLength(60);
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

            var distinctNames = request.RoleNames.Distinct().ToList();

            var roles = await _db.Roles
                .Where(r => distinctNames.Contains(r.Name))
                .ToListAsync(cancellationToken);

            var notFound = distinctNames.Except(roles.Select(r => r.Name)).ToList();
            if (notFound.Count > 0)
                return Result.Failure<RoleAssignmentResponse>(
                    Error.CreateNotFoundError($"Role(s) not found: {string.Join(", ", notFound)}."));

            var assignerIdStr = _auth.GetUserId();
            var assignedAt = DateTime.UtcNow;
            var assignerId = assignerIdStr is not null ? Guid.Parse(assignerIdStr) : (Guid?)null;

            foreach (var role in roles)
            {
                var alreadyAssigned = user.UserRoles.Any(ur => ur.RoleId == role.Id);
                if (alreadyAssigned) continue;

                _db.UserRoles.Add(new UserRole
                {
                    UserId = user.Id,
                    RoleId = role.Id,
                    AssignedAt = assignedAt,
                    AssignedByUserId = assignerId
                });
            }

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
        .WithSummary("Assign one or more roles to a user")
        .WithDescription("Assigns multiple roles in a single request. Roles already assigned are silently skipped — no error. Returns the user's full roles list after assignment.")
        .Produces<AssignRole.RoleAssignmentResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
