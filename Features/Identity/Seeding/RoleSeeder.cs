using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Identity.Entities;

namespace prohpharmacy_trekking_app.Features.Identity.Seeding;

public static class RoleSeeder
{
    private static readonly Dictionary<string, (string Description, string[] Permissions)> _roles = new()
    {
        ["SuperAdmin"] = (
            "Full system access including configuration and all records.",
            Permissions.All),

        ["OperationsManager"] = (
            "All trekking operations, fleet, customers and reports.",
            [
                Permissions.StaffView, Permissions.TreksViewAll, Permissions.TreksCreate,
                Permissions.TreksAssign, Permissions.TreksStart, Permissions.TreksComplete,
                Permissions.VehiclesManage, Permissions.TrackingDevicesManage,
                Permissions.CustomersRegister, Permissions.CustomersEdit, Permissions.CustomersApprove,
                Permissions.CustomerKycView, Permissions.CustomerKycManage,
                Permissions.CustomerCreditView, Permissions.CustomerCreditManage,
                Permissions.VisitsRecord, Permissions.VisitsVerify,
                Permissions.TrackingViewAll, Permissions.ReportsExport
            ]),

        ["BranchManager"] = (
            "Staff, vehicles, treks and customers for assigned branches.",
            [
                Permissions.StaffView, Permissions.StaffManage,
                Permissions.TreksCreate, Permissions.TreksAssign, Permissions.TreksStart, Permissions.TreksComplete,
                Permissions.VehiclesManage,
                Permissions.CustomersRegister, Permissions.CustomersEdit, Permissions.CustomersApprove,
                Permissions.CustomerKycView,
                Permissions.VisitsRecord, Permissions.VisitsVerify,
                Permissions.ReportsExport
            ]),

        ["FieldStaff"] = (
            "Assigned treks, customer registration and visit capture.",
            [
                Permissions.TreksViewAll, Permissions.CustomersRegister, Permissions.VisitsRecord
            ]),

        ["Driver"] = (
            "Assigned treks and personal tracking-device status.",
            [
                Permissions.TreksViewAll
            ]),

        ["CreditOfficer"] = (
            "Customer KYC, credit assessment and outstanding credit records.",
            [
                Permissions.CustomersRegister,
                Permissions.CustomerKycView, Permissions.CustomerKycManage,
                Permissions.CustomerCreditView, Permissions.CustomerCreditManage
            ]),

        ["Auditor"] = (
            "Read-only reports and audit events.",
            [
                Permissions.StaffView, Permissions.CustomerKycView, Permissions.CustomerCreditView,
                Permissions.ReportsExport, Permissions.AuditView
            ])
    };

    public static async Task SeedAsync(AppDbContext db)
    {
        foreach (var (roleName, (description, permissions)) in _roles)
        {
            var role = await db.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Name == roleName);

            if (role is null)
            {
                role = new Role { Name = roleName, Description = description, IsSystem = true };
                db.Roles.Add(role);
                await db.SaveChangesAsync();
            }

            var existingPerms = role.RolePermissions.Select(rp => rp.Permission).ToHashSet();

            foreach (var permission in permissions)
            {
                if (!existingPerms.Contains(permission))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
                }
            }
        }

        await db.SaveChangesAsync();
    }
}
