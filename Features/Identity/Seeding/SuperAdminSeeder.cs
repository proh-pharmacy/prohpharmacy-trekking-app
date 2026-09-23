using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Organisation.Enums;
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Features.Staff.Enums;

namespace prohpharmacy_trekking_app.Features.Identity.Seeding;

public static class SuperAdminSeeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        var email = config["SEED_ADMIN_EMAIL"];
        var password = config["SEED_ADMIN_PASSWORD"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        if (await db.ApplicationUsers.AnyAsync())
            return;

        var firstName = config["SEED_ADMIN_FIRST_NAME"] ?? "Super";
        var lastName = config["SEED_ADMIN_LAST_NAME"] ?? "Admin";

        // Use the first branch, or create a Head Office placeholder
        var branch = await db.Branches.FirstOrDefaultAsync();
        if (branch is null)
        {
            var region = await db.Regions.FirstOrDefaultAsync();
            var district = region is not null
                ? await db.Districts.FirstOrDefaultAsync(d => d.RegionId == region.Id)
                : null;

            if (region is null || district is null)
                return;

            branch = new Branch
            {
                Code = "HQ-001",
                Name = "Head Office",
                BranchType = BranchType.Retail,
                RegionId = region.Id,
                DistrictId = district.Id,
                Address = "Head Office",
                ContactNumber = "N/A",
                Latitude = 0,
                Longitude = 0
            };
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
        }

        var staff = new StaffMember
        {
            FirstName = firstName,
            LastName = lastName,
            EmailAddress = email,
            PhoneNumber = "N/A",
            BranchId = branch.Id,
            EmploymentStatus = EmploymentStatus.Active,
            JoinedOn = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.StaffMembers.Add(staff);
        await db.SaveChangesAsync();

        var user = new ApplicationUser
        {
            StaffMemberId = staff.Id,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();

        var superAdminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "SuperAdmin");
        if (superAdminRole is not null)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = superAdminRole.Id,
                AssignedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }
}
