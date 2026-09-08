using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Services.Email;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Staff.CreateStaff;

namespace prohpharmacy_trekking_app.Features.Staff;

public static class GrantStaffAccess
{
    public class Command : IRequest<Result<StaffResponse>>
    {
        public Guid StaffMemberId { get; set; }
        public string? InitialPassword { get; set; }
        public string? Role { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.InitialPassword)
                .MinimumLength(8)
                .WithMessage("Initial password must be at least 8 characters.")
                .When(x => x.InitialPassword is not null);
            RuleFor(x => x.Role)
                .MaximumLength(60)
                .When(x => x.Role is not null);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<StaffResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;
        private readonly AuthProvider _auth;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;

        public Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth,
            IEmailService email, IConfiguration config)
        {
            _db = db;
            _validator = validator;
            _auth = auth;
            _email = email;
            _config = config;
        }

        public async Task<Result<StaffResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<StaffResponse>(Error.ValidationError(validation));

            var staff = await _db.StaffMembers
                .Include(s => s.Branch)
                .Include(s => s.ApplicationUser)
                .FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);

            if (staff is null)
                return Result.Failure<StaffResponse>(Error.CreateNotFoundError("Staff member not found."));

            if (staff.ApplicationUser is not null)
                return Result.Failure<StaffResponse>(Error.Conflict("This staff member already has app access."));

            if (staff.EmploymentStatus == Enums.EmploymentStatus.Offboarded)
                return Result.Failure<StaffResponse>(Error.BadRequest("Cannot grant access to an offboarded staff member."));

            // Resolve role: use request role if provided, fall back to existing staff.Role
            var roleName = request.Role?.Trim() ?? staff.Role;
            if (string.IsNullOrWhiteSpace(roleName))
                return Result.Failure<StaffResponse>(
                    Error.BadRequest("A role is required. This staff member has no role assigned yet — provide one in the request."));

            var role = await _db.Roles
                .FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken);
            if (role is null)
                return Result.Failure<StaffResponse>(Error.CreateNotFoundError($"Role '{roleName}' not found."));

            var creatorId = _auth.GetUserId();
            var creatorGuid = creatorId is not null ? Guid.Parse(creatorId) : (Guid?)null;

            staff.Role = role.Name;
            staff.EmploymentStatus = Enums.EmploymentStatus.Active;
            staff.UpdatedAt = DateTime.UtcNow;

            var plainPassword = await StaffAccessHelper.GrantAccessAsync(
                _db, _email, _config, staff, [role], request.InitialPassword, creatorGuid, cancellationToken);

            var response = CreateStaff.Handler.ToResponse(staff, staff.Branch?.Name ?? string.Empty, true, [role.Name]);
            response.InitialPassword = plainPassword;
            return Result.Success(response);
        }
    }
}

public class GrantStaffAccessEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/staff/{id:guid}/grant-access", async (
            Guid id, GrantStaffAccess.Command command, ISender sender) =>
        {
            command.StaffMemberId = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Staff")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Staff)
        .WithSummary("Grant app access to an existing staff member")
        .WithDescription(
            "Creates a login account for a staff member who was onboarded without app access. " +
            "If the staff member already has a role assigned, it is used automatically — " +
            "provide `role` to override or assign one for the first time. " +
            "Provide `initialPassword` or leave it null to auto-derive it as `firstname + lastname`. " +
            "Sends a welcome email and flips the employment status to Active.")
        .Produces<StaffResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
