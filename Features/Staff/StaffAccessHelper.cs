using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Services.Email;

namespace prohpharmacy_trekking_app.Features.Staff;

internal static class StaffAccessHelper
{
    internal static async Task<string> GrantAccessAsync(
        AppDbContext db,
        IEmailService email,
        IConfiguration config,
        StaffMember staff,
        List<Role> roles,
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

        db.UserRoles.AddRange(roles.Select(r => new UserRole
        {
            UserId = appUser.Id,
            RoleId = r.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedByUserId = creatorId
        }));

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
