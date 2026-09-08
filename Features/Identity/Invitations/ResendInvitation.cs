using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Email;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Invitations;

public static class ResendInvitation
{
    public class Command : IRequest<Result<ResendResponse>>
    {
        public Guid StaffMemberId { get; set; }
    }

    public class ResendResponse
    {
        public Guid StaffMemberId { get; set; }
        public string StaffFullName { get; set; } = string.Empty;
        public string StaffEmail { get; set; } = string.Empty;
        public string? InitialPassword { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    internal sealed class Handler : IRequestHandler<Command, Result<ResendResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;

        public Handler(AppDbContext db, IEmailService email, IConfiguration config)
        {
            _db = db;
            _email = email;
            _config = config;
        }

        public async Task<Result<ResendResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await _db.ApplicationUsers
                .Include(u => u.StaffMember)
                .FirstOrDefaultAsync(u => u.StaffMemberId == request.StaffMemberId, cancellationToken);

            if (user is null)
                return Result.Failure<ResendResponse>(
                    Error.CreateNotFoundError("No app account found for this staff member."));

            var staff = user.StaffMember;
            var plainPassword = $"{staff.FirstName.ToLower()}{staff.LastName.ToLower()}";

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            var appName = _config["SiteSettings:AppName"] ?? "Proh Pharmacy Trekking";
            var loginUrl = _config["SiteSettings:FrontendUrl"] ?? string.Empty;
            var supportEmail = _config["EmailSettings:SupportEmail"] ?? string.Empty;

            _ = _email.SendStaffWelcomeEmailAsync(staff.EmailAddress, new StaffWelcomeEmailModel
            {
                StaffFullName = staff.FullName,
                EmployeeNumber = staff.EmployeeNumber ?? string.Empty,
                Email = staff.EmailAddress,
                InitialPassword = plainPassword,
                LoginUrl = loginUrl,
                AppName = appName,
                SupportEmail = supportEmail
            });

            return Result.Success(new ResendResponse
            {
                StaffMemberId = staff.Id,
                StaffFullName = staff.FullName,
                StaffEmail = staff.EmailAddress,
                InitialPassword = plainPassword,
                Message = $"Welcome email resent to {staff.EmailAddress} with a reset password."
            });
        }
    }
}

public class ResendInvitationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/invitations/resend", async (ResendInvitation.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Resend welcome email with a reset password")
        .WithDescription(
            "Resets the staff member's password to `firstnamelastname` and resends the welcome email with the new credentials. " +
            "Use this when a staff member has lost access or never logged in.")
        .Produces<ResendInvitation.ResendResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
