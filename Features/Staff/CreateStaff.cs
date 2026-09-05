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
        public string EmployeeNumber { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public Guid BranchId { get; set; }
        public DateOnly JoinedOn { get; set; }
        public bool GrantAppAccess { get; set; }
        public string? InitialPassword { get; set; }
    }

    public class StaffResponse
    {
        public Guid Id { get; set; }
        public string EmployeeNumber { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string EmploymentStatus { get; set; } = string.Empty;
        public DateOnly JoinedOn { get; set; }
        public bool HasAppAccess { get; set; }
        public string? InitialPassword { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.EmployeeNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.EmailAddress).NotEmpty().EmailAddress().MaximumLength(200);
            RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(100);
            RuleFor(x => x.BranchId).NotEmpty();
            RuleFor(x => x.JoinedOn).NotEmpty();
            RuleFor(x => x.InitialPassword)
                .MinimumLength(8)
                .When(x => x.InitialPassword is not null)
                .WithMessage("Initial password must be at least 8 characters.");
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

            var numberTaken = await _db.StaffMembers
                .AnyAsync(s => s.EmployeeNumber == request.EmployeeNumber.Trim().ToUpper(), cancellationToken);
            if (numberTaken)
                return Result.Failure<StaffResponse>(Error.Conflict("Employee number already exists."));

            var emailTaken = await _db.StaffMembers
                .AnyAsync(s => s.EmailAddress.ToLower() == request.EmailAddress.Trim().ToLower(), cancellationToken);
            if (emailTaken)
                return Result.Failure<StaffResponse>(Error.Conflict("Email address is already registered."));

            var creatorId = _auth.GetUserId();

            var staff = new StaffMember
            {
                EmployeeNumber = request.EmployeeNumber.Trim().ToUpper(),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                PhoneNumber = request.PhoneNumber.Trim(),
                EmailAddress = request.EmailAddress.Trim().ToLower(),
                JobTitle = request.JobTitle.Trim(),
                BranchId = request.BranchId,
                EmploymentStatus = request.GrantAppAccess ? EmploymentStatus.Active : EmploymentStatus.Pending,
                JoinedOn = request.JoinedOn,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = creatorId is not null ? Guid.Parse(creatorId) : null
            };

            _db.StaffMembers.Add(staff);
            await _db.SaveChangesAsync(cancellationToken);

            string? plainPassword = null;

            if (request.GrantAppAccess)
            {
                plainPassword = string.IsNullOrWhiteSpace(request.InitialPassword)
                    ? $"{request.FirstName.Trim().ToLower()}{request.LastName.Trim().ToLower()}"
                    : request.InitialPassword;

                _db.ApplicationUsers.Add(new ApplicationUser
                {
                    StaffMemberId = staff.Id,
                    Email = staff.EmailAddress,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync(cancellationToken);

                var appName = _config["SiteSettings:AppName"] ?? "Proh Pharmacy Trekking";
                var loginUrl = _config["SiteSettings:FrontendUrl"] ?? string.Empty;
                var supportEmail = _config["EmailSettings:SupportEmail"] ?? string.Empty;

                _ = _email.SendStaffWelcomeEmailAsync(staff.EmailAddress, new StaffWelcomeEmailModel
                {
                    StaffFullName = staff.FullName,
                    EmployeeNumber = staff.EmployeeNumber,
                    Email = staff.EmailAddress,
                    InitialPassword = plainPassword,
                    LoginUrl = loginUrl,
                    AppName = appName,
                    SupportEmail = supportEmail
                });
            }

            var response = ToResponse(staff, branch.Name, request.GrantAppAccess);
            response.InitialPassword = plainPassword;
            return Result.Success(response);
        }

        internal static StaffResponse ToResponse(StaffMember s, string branchName, bool hasAppAccess = false) => new()
        {
            Id = s.Id,
            EmployeeNumber = s.EmployeeNumber,
            FirstName = s.FirstName,
            LastName = s.LastName,
            FullName = s.FullName,
            PhoneNumber = s.PhoneNumber,
            EmailAddress = s.EmailAddress,
            JobTitle = s.JobTitle,
            BranchId = s.BranchId,
            BranchName = branchName,
            EmploymentStatus = s.EmploymentStatus.ToString(),
            JoinedOn = s.JoinedOn,
            HasAppAccess = hasAppAccess,
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
            "Creates a staff record. " +
            "Set `grantAppAccess: true` to also create a login account immediately. " +
            "Provide `initialPassword` or leave it null to auto-derive it as `firstname + lastname` (e.g. `johndoe`). " +
            "The plain-text initial password is returned once in the response — store it safely and share it with the staff member.")
        .RequireAuthorization();
    }
}
