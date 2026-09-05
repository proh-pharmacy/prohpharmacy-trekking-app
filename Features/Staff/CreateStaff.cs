using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Providers;
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
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<StaffResponse>>
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
                EmploymentStatus = EmploymentStatus.Pending,
                JoinedOn = request.JoinedOn,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = creatorId is not null ? Guid.Parse(creatorId) : null
            };

            _db.StaffMembers.Add(staff);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(staff, branch.Name));
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
        .WithDescription("Creates a staff record. Use POST /api/v1/invitations to grant application access.")
        .RequireAuthorization();
    }
}
