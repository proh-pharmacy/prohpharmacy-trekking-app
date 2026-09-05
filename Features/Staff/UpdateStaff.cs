using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Staff.CreateStaff;

namespace prohpharmacy_trekking_app.Features.Staff;

public static class UpdateStaff
{
    public class Command : IRequest<Result<StaffResponse>>
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Role { get; set; }
        public Guid BranchId { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.Role).NotEmpty().MaximumLength(60).When(x => x.Role is not null);
            RuleFor(x => x.BranchId).NotEmpty();
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<StaffResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<StaffResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<StaffResponse>(Error.ValidationError(validation));

            var staff = await _db.StaffMembers
                .Include(s => s.Branch)
                .Include(s => s.ApplicationUser)
                .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

            if (staff is null)
                return Result.Failure<StaffResponse>(Error.CreateNotFoundError("Staff member not found."));

            if (staff.EmploymentStatus == Enums.EmploymentStatus.Offboarded)
                return Result.Failure<StaffResponse>(Error.BadRequest("Cannot update an offboarded staff member."));

            var branch = await _db.Branches.FindAsync([request.BranchId], cancellationToken);
            if (branch is null)
                return Result.Failure<StaffResponse>(Error.CreateNotFoundError("Branch not found."));

            if (request.Role is not null)
            {
                var role = await _db.Roles
                    .FirstOrDefaultAsync(r => r.Name == request.Role.Trim(), cancellationToken);
                if (role is null)
                    return Result.Failure<StaffResponse>(Error.CreateNotFoundError($"Role '{request.Role}' not found."));
                staff.Role = role.Name;
            }

            staff.FirstName = request.FirstName.Trim();
            staff.LastName = request.LastName.Trim();
            staff.PhoneNumber = request.PhoneNumber.Trim();
            staff.BranchId = request.BranchId;
            staff.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(
                CreateStaff.Handler.ToResponse(staff, branch.Name, staff.ApplicationUser is not null));
        }
    }
}

public class UpdateStaffEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/staff/{id:guid}", async (Guid id, UpdateStaff.Command command, ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Staff")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Staff)
        .WithSummary("Update a staff member")
        .WithDescription("Updates staff details. `role` must match an existing system role.")
        .RequireAuthorization();
    }
}
