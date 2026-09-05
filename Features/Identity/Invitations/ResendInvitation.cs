using System.Security.Cryptography;
using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Services.Email;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Identity.Invitations.CreateInvitation;

namespace prohpharmacy_trekking_app.Features.Identity.Invitations;

public static class ResendInvitation
{
    public class Command : IRequest<Result<InvitationResponse>>
    {
        public Guid InvitationId { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<InvitationResponse>>
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

        public async Task<Result<InvitationResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var invitation = await _db.StaffInvitations
                .Include(i => i.StaffMember)
                .FirstOrDefaultAsync(i => i.Id == request.InvitationId, cancellationToken);

            if (invitation is null)
                return Result.Failure<InvitationResponse>(Error.CreateNotFoundError("Invitation not found."));

            if (invitation.IsUsed)
                return Result.Failure<InvitationResponse>(
                    Error.BadRequest("This invitation has already been accepted. Use password reset if the user needs access restored."));

            var alreadyHasAccess = await _db.ApplicationUsers
                .AnyAsync(u => u.StaffMemberId == invitation.StaffMemberId, cancellationToken);

            if (alreadyHasAccess)
                return Result.Failure<InvitationResponse>(
                    Error.Conflict("This staff member already has application access."));

            var staff = invitation.StaffMember;

            if (invitation.ExpiresAt > DateTime.UtcNow)
            {
                // Still active — refresh the expiry and resend the same token
                invitation.ExpiresAt = DateTime.UtcNow.AddHours(48);
            }
            else
            {
                // Expired — invalidate old record and issue a fresh token
                invitation.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

                var newToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
                    .Replace("+", "-").Replace("/", "_").Replace("=", "");

                var newInvitation = new StaffInvitation
                {
                    StaffMemberId = staff.Id,
                    Token = newToken,
                    ExpiresAt = DateTime.UtcNow.AddHours(48),
                    CreatedByUserId = invitation.CreatedByUserId
                };

                _db.StaffInvitations.Add(newInvitation);
                await _db.SaveChangesAsync(cancellationToken);

                invitation = newInvitation;
            }

            await _db.SaveChangesAsync(cancellationToken);

            var appName = _config["SiteSettings:AppName"] ?? "Proh Pharmacy Trekking";
            var frontendUrl = _config["SiteSettings:FrontendUrl"] ?? string.Empty;
            var supportEmail = _config["EmailSettings:SupportEmail"] ?? string.Empty;

            _ = _email.SendStaffInvitationEmailAsync(staff.EmailAddress, new StaffInvitationEmailModel
            {
                StaffFullName = staff.FullName,
                InvitationLink = $"{frontendUrl}/accept-invitation?token={invitation.Token}",
                ExpiresAt = invitation.ExpiresAt.ToString("dd MMM yyyy, h:mm tt") + " UTC",
                AppName = appName,
                SupportEmail = supportEmail
            });

            return Result.Success(new InvitationResponse
            {
                InvitationId = invitation.Id,
                StaffMemberId = staff.Id,
                StaffFullName = staff.FullName,
                StaffEmail = staff.EmailAddress,
                ExpiresAt = invitation.ExpiresAt,
                Message = $"Invitation resent to {staff.EmailAddress}."
            });
        }
    }
}

public class ResendInvitationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/invitations/{id:guid}/resend", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new ResendInvitation.Command { InvitationId = id });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Resend an invitation")
        .WithDescription(
            "If the invitation is still active, refreshes its expiry to 48 hours and resends the email. " +
            "If expired, invalidates the old token and issues a brand new one. " +
            "Returns 400 if the invitation was already accepted.")
        .RequireAuthorization();
    }
}
