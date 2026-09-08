using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Staff;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Services.Email;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Staff.CreateStaff;

namespace prohpharmacy_trekking_app.Features.Identity.Invitations;

public static class CreateInvitation
{
    public class Command : IRequest<Result<InvitationResponse>>
    {
        public Guid StaffMemberId { get; set; }
        public List<string> RoleNames { get; set; } = [];
        public string? InitialPassword { get; set; }
    }

    public class InvitationResponse
    {
        public Guid StaffMemberId { get; set; }
        public string StaffFullName { get; set; } = string.Empty;
        public string StaffEmail { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = [];
        public string? InitialPassword { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.StaffMemberId).NotEmpty();
            RuleFor(x => x.RoleNames).NotEmpty().WithMessage("At least one role is required.");
            RuleForEach(x => x.RoleNames).NotEmpty().MaximumLength(60);
            RuleFor(x => x.InitialPassword)
                .MinimumLength(8).WithMessage("Initial password must be at least 8 characters.")
                .When(x => x.InitialPassword is not null);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<InvitationResponse>>
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

        public async Task<Result<InvitationResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<InvitationResponse>(Error.ValidationError(validation));

            var staff = await _db.StaffMembers
                .Include(s => s.Branch)
                .Include(s => s.ApplicationUser)
                .FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);

            if (staff is null)
                return Result.Failure<InvitationResponse>(Error.CreateNotFoundError("Staff member not found."));

            if (staff.ApplicationUser is not null)
                return Result.Failure<InvitationResponse>(Error.Conflict("This staff member already has app access."));

            if (staff.EmploymentStatus == Staff.Enums.EmploymentStatus.Offboarded)
                return Result.Failure<InvitationResponse>(Error.BadRequest("Cannot invite an offboarded staff member."));

            var distinctRoleNames = request.RoleNames.Distinct().ToList();
            var roles = await _db.Roles
                .Where(r => distinctRoleNames.Contains(r.Name))
                .ToListAsync(cancellationToken);

            var notFound = distinctRoleNames.Except(roles.Select(r => r.Name)).ToList();
            if (notFound.Count > 0)
                return Result.Failure<InvitationResponse>(
                    Error.CreateNotFoundError($"Role(s) not found: {string.Join(", ", notFound)}."));

            var creatorId = _auth.GetUserId();
            var creatorGuid = creatorId is not null ? Guid.Parse(creatorId) : (Guid?)null;

            staff.EmploymentStatus = Staff.Enums.EmploymentStatus.Active;
            staff.UpdatedAt = DateTime.UtcNow;

            var plainPassword = await StaffAccessHelper.GrantAccessAsync(
                _db, _email, _config, staff, roles, request.InitialPassword, creatorGuid, cancellationToken);

            return Result.Success(new InvitationResponse
            {
                StaffMemberId = staff.Id,
                StaffFullName = staff.FullName,
                StaffEmail = staff.EmailAddress,
                Roles = distinctRoleNames,
                InitialPassword = plainPassword
            });
        }
    }
}

public class CreateInvitationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/invitations", async (CreateInvitation.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/staff/{result.Value.StaffMemberId}", result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Invite a staff member (create account + send welcome email)")
        .WithDescription(
            "Creates a login account immediately and sends a welcome email with the credentials. " +
            "The staff member can log in right away — no acceptance step required. " +
            "Provide `initialPassword` or leave null to auto-derive it as `firstnamelastname`.")
        .Produces<CreateInvitation.InvitationResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
