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
using prohpharmacy_trekking_app.Features.Staff.Enums;
using static prohpharmacy_trekking_app.Features.Identity.Auth.Login;

namespace prohpharmacy_trekking_app.Features.Identity.Invitations;

public static class AcceptInvitation
{
    public class Command : IRequest<Result<AuthResponse>>
    {
        public string Token { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Token).NotEmpty();
            RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
            RuleFor(x => x.ConfirmPassword).Equal(x => x.Password).WithMessage("Passwords do not match.");
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

            var invitation = await _db.StaffInvitations
                .Include(i => i.StaffMember)
                .FirstOrDefaultAsync(i => i.Token == request.Token, cancellationToken);

            if (invitation is null || !invitation.IsValid)
                return Result.Failure<AuthResponse>(Error.BadRequest("Invitation token is invalid or has expired."));

            var staff = invitation.StaffMember;

            if (staff.EmploymentStatus == EmploymentStatus.Offboarded)
                return Result.Failure<AuthResponse>(Error.BadRequest("Cannot activate an offboarded staff member."));

            var user = new ApplicationUser
            {
                StaffMemberId = staff.Id,
                Email = staff.EmailAddress,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.ApplicationUsers.Add(user);

            if (staff.EmploymentStatus == EmploymentStatus.Pending)
            {
                staff.EmploymentStatus = EmploymentStatus.Active;
                staff.UpdatedAt = DateTime.UtcNow;
            }

            invitation.IsUsed = true;
            invitation.UsedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            // Auto-assign the role stored on the staff record
            var systemRole = await _db.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Name == staff.Role, cancellationToken);

            var roles = new List<string>();
            var permissions = new List<string>();

            if (systemRole is not null)
            {
                _db.UserRoles.Add(new UserRole
                {
                    UserId = user.Id,
                    RoleId = systemRole.Id,
                    AssignedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync(cancellationToken);

                roles.Add(systemRole.Name);
                permissions = systemRole.RolePermissions.Select(rp => rp.Permission).ToList();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Email, user.Email),
                new("staff_id", staff.Id.ToString()),
                new(ClaimTypes.Name, staff.FullName)
            };

            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            claims.AddRange(permissions.Select(p => new Claim("permission", p)));

            var accessExpiry = DateTime.UtcNow.AddMinutes(15);
            var accessToken = _jwt.GenerateToken(claims, 15);

            var refreshValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var refreshExpiry = DateTime.UtcNow.AddDays(7);

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                Token = refreshValue,
                ExpiresAt = refreshExpiry
            });

            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshValue,
                AccessTokenExpiresAt = accessExpiry,
                RefreshTokenExpiresAt = refreshExpiry,
                User = new UserInfo
                {
                    UserId = user.Id,
                    StaffMemberId = staff.Id,
                    Email = user.Email,
                    FullName = staff.FullName,
                    Roles = roles,
                    Permissions = permissions
                }
            });
        }
    }
}

public class AcceptInvitationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/invitations/accept", async (AcceptInvitation.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Accept an invitation and set password")
        .WithDescription("Validates the invitation token, creates the user account, auto-assigns the staff role, and returns an initial auth token pair.")
        .AllowAnonymous();
    }
}
