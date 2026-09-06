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

namespace prohpharmacy_trekking_app.Features.Identity.Auth;

public static class Login
{
    public class Command : IRequest<Result<AuthResponse>>
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class AuthResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime AccessTokenExpiresAt { get; set; }
        public DateTime RefreshTokenExpiresAt { get; set; }
        public UserInfo User { get; set; } = null!;
    }

    public class UserInfo
    {
        public Guid UserId { get; set; }
        public Guid StaffMemberId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = [];
        public List<string> Permissions { get; set; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.Password).NotEmpty();
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

            var user = await _db.ApplicationUsers
                .Include(u => u.StaffMember)
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                        .ThenInclude(r => r.RolePermissions)
                .FirstOrDefaultAsync(u => u.Email == request.Email.Trim().ToLower(), cancellationToken);

            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Result.Failure<AuthResponse>(Error.BadRequest("Invalid email or password."));

            if (!user.IsActive)
                return Result.Failure<AuthResponse>(Error.Forbidden("Account is suspended or inactive."));

            if (user.LockoutUntil.HasValue && user.LockoutUntil > DateTime.UtcNow)
                return Result.Failure<AuthResponse>(Error.Forbidden("Account is temporarily locked."));

            user.LastLoginAt = DateTime.UtcNow;
            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;

            var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
            var permissions = user.UserRoles
                .SelectMany(ur => ur.Role.RolePermissions)
                .Select(rp => rp.Permission)
                .Distinct()
                .ToList();

            var (accessToken, accessExpiry) = GenerateAccessToken(user, roles, permissions);
            var (refreshTokenValue, refreshExpiry) = GenerateRefreshToken(user);

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshTokenValue,
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

        private (string token, DateTime expiry) GenerateAccessToken(
            ApplicationUser user, List<string> roles, List<string> permissions)
        {
            var expiry = DateTime.UtcNow.AddMinutes(15);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Email, user.Email),
                new("staff_id", user.StaffMemberId.ToString()),
                new(ClaimTypes.Name, user.StaffMember.FullName)
            };

            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            claims.AddRange(permissions.Select(p => new Claim("permission", p)));

            var token = _jwt.GenerateToken(claims, 15);
            return (token, expiry);
        }

        private (string token, DateTime expiry) GenerateRefreshToken(ApplicationUser user)
        {
            var tokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var expiry = DateTime.UtcNow.AddDays(7);

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                Token = tokenValue,
                ExpiresAt = expiry
            });

            return (tokenValue, expiry);
        }
    }
}

public class LoginEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/auth/login", async (Login.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Log in")
        .WithDescription("Returns a short-lived access token (15 min) and a 7-day rotating refresh token.")
        .Produces<Login.AuthResponse>(200)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
