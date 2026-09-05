using System.Security.Cryptography;
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

namespace prohpharmacy_trekking_app.Features.Identity.Invitations;

public static class CreateInvitation
{
    public class Command : IRequest<Result<InvitationResponse>>
    {
        public Guid StaffMemberId { get; set; }
    }

    public class InvitationResponse
    {
        public Guid InvitationId { get; set; }
        public Guid StaffMemberId { get; set; }
        public string StaffFullName { get; set; } = string.Empty;
        public string StaffEmail { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.StaffMemberId).NotEmpty();
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
                .FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);

            if (staff is null)
                return Result.Failure<InvitationResponse>(Error.CreateNotFoundError("Staff member not found."));

            var alreadyHasAccess = await _db.ApplicationUsers
                .AnyAsync(u => u.StaffMemberId == request.StaffMemberId, cancellationToken);

            if (alreadyHasAccess)
                return Result.Failure<InvitationResponse>(
                    Error.Conflict("This staff member already has application access."));

            if (staff.EmploymentStatus == Staff.Enums.EmploymentStatus.Offboarded)
                return Result.Failure<InvitationResponse>(
                    Error.BadRequest("Cannot invite an offboarded staff member."));

            var pendingInvitation = await _db.StaffInvitations
                .FirstOrDefaultAsync(i => i.StaffMemberId == request.StaffMemberId && !i.IsUsed && i.ExpiresAt > DateTime.UtcNow,
                    cancellationToken);

            if (pendingInvitation is not null)
            {
                pendingInvitation.ExpiresAt = DateTime.UtcNow.AddHours(48);
                await _db.SaveChangesAsync(cancellationToken);

                var appNameResend = _config["SiteSettings:AppName"] ?? "Proh Pharmacy Trekking";
                var frontendUrlResend = _config["SiteSettings:FrontendUrl"] ?? string.Empty;
                var supportEmailResend = _config["EmailSettings:SupportEmail"] ?? string.Empty;

                _ = _email.SendStaffInvitationEmailAsync(staff.EmailAddress, new StaffInvitationEmailModel
                {
                    StaffFullName = staff.FullName,
                    InvitationLink = $"{frontendUrlResend}/accept-invitation?token={pendingInvitation.Token}",
                    ExpiresAt = pendingInvitation.ExpiresAt.ToString("dd MMM yyyy, h:mm tt") + " UTC",
                    AppName = appNameResend,
                    SupportEmail = supportEmailResend
                });

                return Result.Success(new InvitationResponse
                {
                    InvitationId = pendingInvitation.Id,
                    StaffMemberId = staff.Id,
                    StaffFullName = staff.FullName,
                    StaffEmail = staff.EmailAddress,
                    ExpiresAt = pendingInvitation.ExpiresAt,
                    Message = "A new invitation email has been sent."
                });
            }

            var creatorId = _auth.GetUserId();
            var tokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
                .Replace("+", "-").Replace("/", "_").Replace("=", "");

            var invitation = new StaffInvitation
            {
                StaffMemberId = staff.Id,
                Token = tokenValue,
                ExpiresAt = DateTime.UtcNow.AddHours(48),
                CreatedByUserId = creatorId is not null ? Guid.Parse(creatorId) : null
            };

            _db.StaffInvitations.Add(invitation);
            await _db.SaveChangesAsync(cancellationToken);

            var appName = _config["SiteSettings:AppName"] ?? "Proh Pharmacy Trekking";
            var frontendUrl = _config["SiteSettings:FrontendUrl"] ?? string.Empty;
            var supportEmail = _config["EmailSettings:SupportEmail"] ?? string.Empty;
            var invitationLink = $"{frontendUrl}/accept-invitation?token={invitation.Token}";

            _ = _email.SendStaffInvitationEmailAsync(staff.EmailAddress, new StaffInvitationEmailModel
            {
                StaffFullName = staff.FullName,
                InvitationLink = invitationLink,
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
                Message = $"Invitation email sent to {staff.EmailAddress}."
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
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Send an application-access invitation")
        .WithDescription("Generates a 48-hour invitation token and emails it directly to the staff member's registered email address.")
        .RequireAuthorization();
    }
}
