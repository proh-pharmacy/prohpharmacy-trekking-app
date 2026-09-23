using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Auth;

public static class ResetPasswordByToken
{
    public class Command : IRequest<Result>
    {
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Token).NotEmpty();
            RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8)
                .WithMessage("Password must be at least 8 characters.");
            RuleFor(x => x.ConfirmNewPassword)
                .Equal(x => x.NewPassword)
                .WithMessage("Passwords do not match.");
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure(Error.ValidationError(validation));

            var user = await _db.ApplicationUsers
                .Include(u => u.RefreshTokens.Where(rt => !rt.IsRevoked))
                .FirstOrDefaultAsync(u => u.PasswordResetToken == request.Token, cancellationToken);

            if (user is null || user.PasswordResetTokenExpiresAt < DateTime.UtcNow)
                return Result.Failure(Error.BadRequest("This reset link is invalid or has expired."));

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiresAt = null;
            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;
            user.UpdatedAt = DateTime.UtcNow;

            foreach (var token in user.RefreshTokens)
            {
                token.IsRevoked = true;
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedReason = "Password reset via token";
            }

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public class ResetPasswordByTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/auth/reset-password", async (ResetPasswordByToken.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(new { message = "Password reset successfully. Please log in with your new password." });
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Reset password using a token")
        .WithDescription(
            "Completes the self-service password reset flow. Validates the token from the reset email, " +
            "updates the password, clears the token, and revokes all active sessions.")
        .Produces(200)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
