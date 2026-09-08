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
        public List<string> RoleNames { get; set; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.InitialPassword)
                .MinimumLength(8)
                .WithMessage("Initial password must be at least 8 characters.")
                .When(x => x.InitialPassword is not null);
            RuleForEach(x => x.RoleNames).NotEmpty().MaximumLength(60);
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

            // Resolve roles: use provided list, fall back to staff.Role if none given
            var distinctRoleNames = request.RoleNames.Distinct().ToList();
            if (distinctRoleNames.Count == 0 && !string.IsNullOrWhiteSpace(staff.Role))
                distinctRoleNames = [staff.Role];

            if (distinctRoleNames.Count == 0)
                return Result.Failure<StaffResponse>(
                    Error.BadRequest("At least one role is required. This staff member has no role assigned yet — provide roleNames in the request."));

            var roles = await _db.Roles
                .Where(r => distinctRoleNames.Contains(r.Name))
                .ToListAsync(cancellationToken);

            var notFound = distinctRoleNames.Except(roles.Select(r => r.Name)).ToList();
            if (notFound.Count > 0)
                return Result.Failure<StaffResponse>(
                    Error.CreateNotFoundError($"Role(s) not found: {string.Join(", ", notFound)}."));

            var creatorId = _auth.GetUserId();
            var creatorGuid = creatorId is not null ? Guid.Parse(creatorId) : (Guid?)null;

            staff.EmploymentStatus = Enums.EmploymentStatus.Active;
            staff.UpdatedAt = DateTime.UtcNow;

            var plainPassword = await StaffAccessHelper.GrantAccessAsync(
                _db, _email, _config, staff, roles, request.InitialPassword, creatorGuid, cancellationToken);

            var roleNames = roles.Select(r => r.Name).ToList();
            var response = CreateStaff.Handler.ToResponse(staff, staff.Branch?.Name ?? string.Empty, true, roleNames);
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
            "Creates a login account and assigns the specified roles. " +
            "If `roleNames` is omitted, falls back to the role already on the staff record. " +
            "Provide `initialPassword` or leave null to auto-derive as `firstnamelastname`. " +
            "Sends a welcome email and sets employment status to Active.")
        .Produces<StaffResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
