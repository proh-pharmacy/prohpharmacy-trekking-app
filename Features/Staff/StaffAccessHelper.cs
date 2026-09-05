using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Services.Email;

namespace prohpharmacy_trekking_app.Features.Staff;

internal static class StaffAccessHelper
{
    /// <summary>
    /// Creates an ApplicationUser + UserRole for the given staff member, then fires the welcome email.
    /// Any pending changes on <paramref name="staff"/> (role name, status, etc.) are persisted in the
    /// first SaveChanges call inside this method — callers do not need a separate save beforehand.
    /// Returns the plain-text password that was used.
    /// </summary>
    internal static async Task<string> GrantAccessAsync(
        AppDbContext db,
        IEmailService email,
        IConfiguration config,
        StaffMember staff,
        Role role,
        string? requestedPassword,
        Guid? creatorId,
        CancellationToken cancellationToken)
    {
        var plainPassword = string.IsNullOrWhiteSpace(requestedPassword)
            ? $"{staff.FirstName.ToLower()}{staff.LastName.ToLower()}"
            : requestedPassword;

        var appUser = new ApplicationUser
        {
            StaffMemberId = staff.Id,
            Email = staff.EmailAddress,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.ApplicationUsers.Add(appUser);
        await db.SaveChangesAsync(cancellationToken);

        db.UserRoles.Add(new UserRole
        {
            UserId = appUser.Id,
            RoleId = role.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedByUserId = creatorId
        });

        await db.SaveChangesAsync(cancellationToken);

        var appName = config["SiteSettings:AppName"] ?? "Proh Pharmacy Trekking";
        var loginUrl = config["SiteSettings:FrontendUrl"] ?? string.Empty;
        var supportEmail = config["EmailSettings:SupportEmail"] ?? string.Empty;

        _ = email.SendStaffWelcomeEmailAsync(staff.EmailAddress, new StaffWelcomeEmailModel
        {
            StaffFullName = staff.FullName,
            EmployeeNumber = staff.EmployeeNumber ?? string.Empty,
            Email = staff.EmailAddress,
            InitialPassword = plainPassword,
            LoginUrl = loginUrl,
            AppName = appName,
            SupportEmail = supportEmail
        });

        return plainPassword;
    }
}
