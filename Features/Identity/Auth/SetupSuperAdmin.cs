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
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Features.Staff.Enums;
using static prohpharmacy_trekking_app.Features.Identity.Auth.Login;

namespace prohpharmacy_trekking_app.Features.Identity.Auth;

public static class SetupSuperAdmin
{
    public class Command : IRequest<Result<AuthResponse>>
    {
        public string EmployeeNumber { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public Guid BranchId { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.EmployeeNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
            RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
            RuleFor(x => x.ConfirmPassword).Equal(x => x.Password).WithMessage("Passwords do not match.");
            RuleFor(x => x.BranchId).NotEmpty();
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
            var anyUser = await _db.ApplicationUsers.AnyAsync(cancellationToken);
            if (anyUser)
                return Result.Failure<AuthResponse>(
                    Error.Forbidden("Setup has already been completed. Use the standard login endpoint."));

            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<AuthResponse>(Error.ValidationError(validation));

            var branch = await _db.Branches.FindAsync([request.BranchId], cancellationToken);
            if (branch is null)
                return Result.Failure<AuthResponse>(Error.CreateNotFoundError("Branch not found."));

            var superAdminRole = await _db.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Name == "SuperAdmin", cancellationToken);

            if (superAdminRole is null)
                return Result.Failure<AuthResponse>(
                    Error.ServerError("Roles have not been seeded. Please restart the server."));

            var staff = new StaffMember
            {
                EmployeeNumber = request.EmployeeNumber.Trim().ToUpper(),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                PhoneNumber = request.PhoneNumber.Trim(),
                EmailAddress = request.Email.Trim().ToLower(),
                Role = "SuperAdmin",
                BranchId = request.BranchId,
                EmploymentStatus = EmploymentStatus.Active,
                JoinedOn = DateOnly.FromDateTime(DateTime.UtcNow),
                CreatedAt = DateTime.UtcNow
            };

            _db.StaffMembers.Add(staff);
            await _db.SaveChangesAsync(cancellationToken);

            var user = new ApplicationUser
            {
                StaffMemberId = staff.Id,
                Email = request.Email.Trim().ToLower(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.ApplicationUsers.Add(user);
            await _db.SaveChangesAsync(cancellationToken);

            _db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = superAdminRole.Id,
                AssignedAt = DateTime.UtcNow
            });

            var roles = new List<string> { "SuperAdmin" };
            var permissions = superAdminRole.RolePermissions.Select(rp => rp.Permission).ToList();

            var accessExpiry = DateTime.UtcNow.AddMinutes(15);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Email, user.Email),
                new("staff_id", staff.Id.ToString()),
                new(ClaimTypes.Name, staff.FullName)
            };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            claims.AddRange(permissions.Select(p => new Claim("permission", p)));

            var accessToken = _jwt.GenerateToken(claims, 15);

            var refreshValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var refreshExpiry = DateTime.UtcNow.AddDays(7);

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                Token = refreshValue,
                ExpiresAt = refreshExpiry
            });

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

public class SetupSuperAdminEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/auth/setup", async (SetupSuperAdmin.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created("api/v1/users", result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Initial super-admin setup")
        .WithDescription("One-time endpoint to create the first Super Admin. Returns 403 once any user exists.")
        .Produces<AuthResponse>(201)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
