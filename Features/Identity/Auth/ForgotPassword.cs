using System.Security.Cryptography;
using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Email;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Auth;

public static class ForgotPassword
{
    public class Command : IRequest<Result>
    {
        public string Email { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result>
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

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure(Error.ValidationError(validation));

            var user = await _db.ApplicationUsers
                .Include(u => u.StaffMember)
                .FirstOrDefaultAsync(u => u.Email == request.Email.Trim().ToLower(), cancellationToken);

            // Always return success to avoid email enumeration
            if (user is null || !user.IsActive)
                return Result.Success();

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiresAt = DateTime.UtcNow.AddHours(1);

            user.PasswordResetToken = token;
            user.PasswordResetTokenExpiresAt = expiresAt;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            var frontendUrl = _config["SiteSettings:FrontendUrl"]?.TrimEnd('/') ?? string.Empty;
            var appName = _config["SiteSettings:AppName"] ?? "Proh Pharmacy Trekking";
            var supportEmail = _config["EmailSettings:SupportEmail"] ?? string.Empty;

            _ = _email.SendPasswordResetEmailAsync(user.Email, new PasswordResetEmailModel
            {
                StaffFullName = user.StaffMember.FullName,
                ResetLink = $"{frontendUrl}/auth/reset-password?token={token}",
                ExpiresAt = expiresAt.ToString("dd MMM yyyy, h:mm tt") + " UTC",
                AppName = appName,
                SupportEmail = supportEmail
            });

            return Result.Success();
        }
    }
}

public class ForgotPasswordEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/auth/forgot-password", async (ForgotPassword.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(new { message = "If that email is registered, a reset link has been sent." });
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Request a password reset link")
        .WithDescription(
            "Sends a password reset email to the provided address if it belongs to an active account. " +
            "Always returns 200 to prevent email enumeration. The reset link expires in 1 hour.")
        .Produces(200)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
