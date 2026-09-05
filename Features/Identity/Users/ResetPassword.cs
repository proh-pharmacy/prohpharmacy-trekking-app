using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Email;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Users;

public static class ResetPassword
{
    public class Command : IRequest<Result<ResetPasswordResponse>>
    {
        public Guid UserId { get; set; }
        public bool ResetToDefault { get; set; }
        public string? NewPassword { get; set; }
    }

    public class ResetPasswordResponse
    {
        public Guid UserId { get; set; }
        public string NewPassword { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x)
                .Must(x => x.ResetToDefault || !string.IsNullOrWhiteSpace(x.NewPassword))
                .WithName("Request")
                .WithMessage("Provide either 'resetToDefault: true' or a 'newPassword'.");

            RuleFor(x => x.NewPassword)
                .MinimumLength(8)
                .WithMessage("Password must be at least 8 characters.")
                .When(x => !x.ResetToDefault && x.NewPassword is not null);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<ResetPasswordResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;

        public Handler(AppDbContext db, IValidator<Command> validator,
            IEmailService email, IConfiguration config)
        {
            _db = db;
            _validator = validator;
            _email = email;
            _config = config;
        }

        public async Task<Result<ResetPasswordResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<ResetPasswordResponse>(Error.ValidationError(validation));

            var user = await _db.ApplicationUsers
                .Include(u => u.StaffMember)
                .Include(u => u.RefreshTokens.Where(rt => !rt.IsRevoked))
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user is null)
                return Result.Failure<ResetPasswordResponse>(Error.CreateNotFoundError("User not found."));

            var plainPassword = request.ResetToDefault
                ? $"{user.StaffMember.FirstName.ToLower()}{user.StaffMember.LastName.ToLower()}"
                : request.NewPassword!;

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword);
            user.UpdatedAt = DateTime.UtcNow;
            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;

            foreach (var token in user.RefreshTokens)
            {
                token.IsRevoked = true;
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedReason = "Password reset by admin";
            }

            await _db.SaveChangesAsync(cancellationToken);

            var appName = _config["SiteSettings:AppName"] ?? "Proh Pharmacy Trekking";
            var loginUrl = _config["SiteSettings:FrontendUrl"] ?? string.Empty;
            var supportEmail = _config["EmailSettings:SupportEmail"] ?? string.Empty;

            _ = _email.SendStaffWelcomeEmailAsync(user.Email, new StaffWelcomeEmailModel
            {
                StaffFullName = user.StaffMember.FullName,
                EmployeeNumber = user.StaffMember.EmployeeNumber,
                Email = user.Email,
                InitialPassword = plainPassword,
                LoginUrl = loginUrl,
                AppName = appName,
                SupportEmail = supportEmail
            });

            return Result.Success(new ResetPasswordResponse
            {
                UserId = user.Id,
                NewPassword = plainPassword,
                Message = $"Password reset successfully. A new credentials email has been sent to {user.Email}."
            });
        }
    }
}

public class ResetPasswordEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/users/{id:guid}/reset-password", async (
            Guid id, ResetPassword.Command command, ISender sender) =>
        {
            command.UserId = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Reset a user's password (admin)")
        .WithDescription(
            "Admin action. Resets the user's password, revokes all active sessions, and emails new credentials. " +
            "Set `resetToDefault: true` to restore the auto-derived default (`firstname + lastname`). " +
            "Or provide a custom `newPassword`. Exactly one of the two must be supplied. " +
            "The new plain-text password is returned once in the response.")
        .RequireAuthorization();
    }
}
