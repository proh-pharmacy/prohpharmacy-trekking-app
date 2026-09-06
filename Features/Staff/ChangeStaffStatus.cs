using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Features.Staff.Enums;

namespace prohpharmacy_trekking_app.Features.Staff;

public static class ChangeStaffStatus
{
    public class Command : IRequest<Result<StatusResponse>>
    {
        public Guid Id { get; set; }
        public EmploymentStatus Status { get; set; }
    }

    public class StatusResponse
    {
        public Guid StaffMemberId { get; set; }
        public string EmploymentStatus { get; set; } = string.Empty;
        public bool AppAccessRevoked { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Status).IsInEnum()
                .WithMessage("Valid statuses: Pending, Active, Suspended, Offboarded.");
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<StatusResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;
        private readonly ILogger<Handler> _logger;

        public Handler(AppDbContext db, IValidator<Command> validator, ILogger<Handler> logger)
        {
            _db = db;
            _validator = validator;
            _logger = logger;
        }

        public async Task<Result<StatusResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<StatusResponse>(Error.ValidationError(validation));

            var staff = await _db.StaffMembers
                .Include(s => s.ApplicationUser)
                    .ThenInclude(u => u!.RefreshTokens.Where(rt => !rt.IsRevoked))
                .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

            if (staff is null)
                return Result.Failure<StatusResponse>(Error.CreateNotFoundError("Staff member not found."));

            if (staff.EmploymentStatus == request.Status)
                return Result.Failure<StatusResponse>(
                    Error.BadRequest($"Staff member is already {request.Status}."));

            staff.EmploymentStatus = request.Status;
            staff.UpdatedAt = DateTime.UtcNow;

            var appAccessRevoked = false;

            if (request.Status is EmploymentStatus.Suspended or EmploymentStatus.Offboarded)
            {
                if (staff.ApplicationUser is not null)
                {
                    staff.ApplicationUser.IsActive = false;
                    staff.ApplicationUser.UpdatedAt = DateTime.UtcNow;

                    foreach (var token in staff.ApplicationUser.RefreshTokens)
                    {
                        token.IsRevoked = true;
                        token.RevokedAt = DateTime.UtcNow;
                        token.RevokedReason = $"Staff {request.Status.ToString().ToLower()}";
                    }

                    appAccessRevoked = true;
                }
            }

            if (request.Status == EmploymentStatus.Active && staff.ApplicationUser is not null)
            {
                staff.ApplicationUser.IsActive = true;
                staff.ApplicationUser.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Staff {StaffId} ({Name}) status changed to {Status}{AccessNote}",
                staff.Id, staff.FullName, request.Status,
                appAccessRevoked ? " — app access revoked" : "");

            return Result.Success(new StatusResponse
            {
                StaffMemberId = staff.Id,
                EmploymentStatus = staff.EmploymentStatus.ToString(),
                AppAccessRevoked = appAccessRevoked
            });
        }
    }
}

public class ChangeStaffStatusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/staff/{id:guid}/status", async (Guid id, ChangeStaffStatus.Command command, ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Staff")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Staff)
        .WithSummary("Change staff employment status")
        .WithDescription("Suspending or offboarding immediately revokes app access and all active refresh tokens.")
        .Produces<ChangeStaffStatus.StatusResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
