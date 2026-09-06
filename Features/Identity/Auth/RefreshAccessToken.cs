using System.Security.Claims;
using System.Security.Cryptography;
using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Identity.Auth.Login;

namespace prohpharmacy_trekking_app.Features.Identity.Auth;

public static class RefreshAccessToken
{
    public class Command : IRequest<Result<AuthResponse>>
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.RefreshToken).NotEmpty();
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<AuthResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;
        private readonly JWTProvider _jwt;

        public Handler(AppDbContext db, IValidator<Command> validator, JWTProvider jwt)
        {
            _db = db;
            _validator = validator;
            _jwt = jwt;
        }

        public async Task<Result<AuthResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<AuthResponse>(Error.ValidationError(validation));

            var stored = await _db.RefreshTokens
                .Include(rt => rt.User)
                    .ThenInclude(u => u.StaffMember)
                .Include(rt => rt.User)
                    .ThenInclude(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                            .ThenInclude(r => r.RolePermissions)
                .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken, cancellationToken);

            if (stored is null || !stored.IsActive)
                return Result.Failure<AuthResponse>(Error.BadRequest("Refresh token is invalid or expired."));

            if (!stored.User.IsActive)
                return Result.Failure<AuthResponse>(Error.Forbidden("Account is suspended."));

            stored.IsRevoked = true;
            stored.RevokedAt = DateTime.UtcNow;
            stored.RevokedReason = "Rotated";

            var user = stored.User;
            var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
            var permissions = user.UserRoles
                .SelectMany(ur => ur.Role.RolePermissions)
                .Select(rp => rp.Permission)
                .Distinct()
                .ToList();

            var accessExpiry = DateTime.UtcNow.AddMinutes(15);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Email, user.Email),
                new("staff_id", user.StaffMemberId.ToString()),
                new(ClaimTypes.Name, user.StaffMember.FullName)
            };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            claims.AddRange(permissions.Select(p => new Claim("permission", p)));

            var newAccessToken = _jwt.GenerateToken(claims, 15);

            var newRefreshValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var refreshExpiry = DateTime.UtcNow.AddDays(7);

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                Token = newRefreshValue,
                ExpiresAt = refreshExpiry
            });

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new AuthResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshValue,
                AccessTokenExpiresAt = accessExpiry,
                RefreshTokenExpiresAt = refreshExpiry,
                User = new UserInfo
                {
                    UserId = user.Id,
                    StaffMemberId = user.StaffMemberId,
                    Email = user.Email,
                    FullName = user.StaffMember.FullName,
                    Roles = roles,
                    Permissions = permissions
                }
            });
        }
    }
}

public class RefreshAccessTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/auth/refresh", async (RefreshAccessToken.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Refresh access token")
        .WithDescription("Consumes and rotates the provided refresh token. Returns a new access + refresh token pair.")
        .Produces<AuthResponse>(200)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
