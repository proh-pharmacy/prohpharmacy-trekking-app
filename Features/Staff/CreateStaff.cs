using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Services.Email;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Features.Staff.Enums;

namespace prohpharmacy_trekking_app.Features.Staff;

public static class CreateStaff
{
    public class Command : IRequest<Result<StaffResponse>>
    {
        public string? EmployeeNumber { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public Guid BranchId { get; set; }
        public DateOnly JoinedOn { get; set; }
        public bool GrantAppAccess { get; set; }
        public string? InitialPassword { get; set; }
        public string? Role { get; set; }
    }

    public class StaffResponse
    {
        public Guid Id { get; set; }
        public string? EmployeeNumber { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public string? Role { get; set; }
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string EmploymentStatus { get; set; } = string.Empty;
        public DateOnly JoinedOn { get; set; }
        public bool HasAppAccess { get; set; }
        public List<string> SystemRoles { get; set; } = [];
        public Guid? CurrentDeviceId { get; set; }
        public string? CurrentDeviceName { get; set; }
        public string? InitialPassword { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.EmployeeNumber).MaximumLength(30).When(x => x.EmployeeNumber is not null);
            RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.EmailAddress).NotEmpty().EmailAddress().MaximumLength(200);
            RuleFor(x => x.Role)
                .NotEmpty().WithMessage("Role is required when granting app access.")
                .MaximumLength(60)
                .When(x => x.GrantAppAccess);
            RuleFor(x => x.Role)
                .MaximumLength(60)
                .When(x => !x.GrantAppAccess && x.Role is not null);
            RuleFor(x => x.BranchId).NotEmpty();
            RuleFor(x => x.JoinedOn).NotEmpty();
            RuleFor(x => x.InitialPassword)
                .MinimumLength(8)
                .WithMessage("Initial password must be at least 8 characters.")
                .When(x => x.InitialPassword is not null);
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

            var branch = await _db.Branches.FindAsync([request.BranchId], cancellationToken);
            if (branch is null)
                return Result.Failure<StaffResponse>(Error.CreateNotFoundError("Branch not found."));

            if (!branch.IsActive)
                return Result.Failure<StaffResponse>(Error.BadRequest("Cannot assign staff to an inactive branch."));

            // Validate role exists before any DB writes (only needed when granting access)
            Role? role = null;
            if (request.GrantAppAccess)
            {
                role = await _db.Roles
                    .FirstOrDefaultAsync(r => r.Name == request.Role!.Trim(), cancellationToken);
                if (role is null)
                    return Result.Failure<StaffResponse>(Error.CreateNotFoundError($"Role '{request.Role}' not found."));
            }

            if (request.EmployeeNumber is not null)
            {
                var numberTaken = await _db.StaffMembers
                    .AnyAsync(s => s.EmployeeNumber == request.EmployeeNumber.Trim().ToUpper(), cancellationToken);
                if (numberTaken)
                    return Result.Failure<StaffResponse>(Error.Conflict("Employee number already exists."));
            }

            var emailTaken = await _db.StaffMembers
                .AnyAsync(s => s.EmailAddress.ToLower() == request.EmailAddress.Trim().ToLower(), cancellationToken);
            if (emailTaken)
                return Result.Failure<StaffResponse>(Error.Conflict("Email address is already registered."));

            var creatorId = _auth.GetUserId();

            var staff = new StaffMember
            {
                EmployeeNumber = request.EmployeeNumber?.Trim().ToUpper(),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                PhoneNumber = request.PhoneNumber.Trim(),
                EmailAddress = request.EmailAddress.Trim().ToLower(),
                Role = role?.Name ?? request.Role?.Trim(),
                BranchId = request.BranchId,
                EmploymentStatus = request.GrantAppAccess ? EmploymentStatus.Active : EmploymentStatus.Pending,
                JoinedOn = request.JoinedOn,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = creatorId is not null ? Guid.Parse(creatorId) : null
            };

            _db.StaffMembers.Add(staff);
            await _db.SaveChangesAsync(cancellationToken);

            string? plainPassword = null;
            var assignedRoles = new List<string>();

            if (request.GrantAppAccess)
            {
                var creatorGuid = creatorId is not null ? Guid.Parse(creatorId) : (Guid?)null;
                plainPassword = await StaffAccessHelper.GrantAccessAsync(
                    _db, _email, _config, staff, role!, request.InitialPassword, creatorGuid, cancellationToken);
                assignedRoles.Add(role!.Name);
            }

            var response = ToResponse(staff, branch.Name, request.GrantAppAccess, assignedRoles);
            response.InitialPassword = plainPassword;
            return Result.Success(response);
        }

        internal static StaffResponse ToResponse(
            StaffMember s, string branchName, bool hasAppAccess = false,
            List<string>? systemRoles = null, Guid? currentDeviceId = null, string? currentDeviceName = null) => new()
        {
            Id = s.Id,
            EmployeeNumber = s.EmployeeNumber,
            FirstName = s.FirstName,
            LastName = s.LastName,
            FullName = s.FullName,
            PhoneNumber = s.PhoneNumber,
            EmailAddress = s.EmailAddress,
            Role = s.Role,
            BranchId = s.BranchId,
            BranchName = branchName,
            EmploymentStatus = s.EmploymentStatus.ToString(),
            JoinedOn = s.JoinedOn,
            HasAppAccess = hasAppAccess,
            SystemRoles = systemRoles ?? [],
            CurrentDeviceId = currentDeviceId,
            CurrentDeviceName = currentDeviceName,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        };
    }
}

public class CreateStaffEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/staff", async (CreateStaff.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/staff/{result.Value.Id}", result.Value);
        })
        .WithTags("Staff")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Staff)
        .WithSummary("Create a new staff member")
        .WithDescription(
            "Creates a staff record. `role` is required and must match an existing system role (e.g. `FieldStaff`, `Driver`). " +
            "Set `grantAppAccess: true` to also create a login account and assign the role in the same request. " +
            "Provide `initialPassword` or leave it null to auto-derive it as `firstname + lastname` (e.g. `johndoe`). " +
            "The plain-text initial password is returned once in the response.")
        .RequireAuthorization();
    }
}
