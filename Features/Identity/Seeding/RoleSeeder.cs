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
                Permissions.StaffView, Permissions.StaffEdit,
                Permissions.BranchesView,
                Permissions.ProductsView, Permissions.ProductsEdit,
                Permissions.CustomersView, Permissions.CustomersEdit, Permissions.CustomersApprove,
                Permissions.TreksViewAll, Permissions.TreksViewDetails,
                Permissions.TreksCreate, Permissions.TreksEdit, Permissions.TreksAssign,
                Permissions.TreksStart, Permissions.TreksComplete, Permissions.TreksCancel,
                Permissions.TreksExport, Permissions.TreksDownloadSheet,
                Permissions.TreksGenerateDriverLink, Permissions.TreksSendEmail,
                Permissions.TrekStopsView, Permissions.TrekStopsAdd, Permissions.TrekStopsEdit,
                Permissions.TrekStopsDelete, Permissions.TrekStopsReorder, Permissions.TrekStopsChangeCustomer,
                Permissions.TrekProductsView, Permissions.TrekProductsAdd, Permissions.TrekProductsEdit,
                Permissions.TrekProductsDelete, Permissions.TrekProductsOverridePrice,
                Permissions.TrekDeliveriesView, Permissions.TrekReturnsView,
                Permissions.VehiclesView,
                Permissions.TrackingViewAll,
                Permissions.ReportsView, Permissions.ReportsExport
            ]),

        ["BranchManager"] = (
            "Staff, vehicles, treks and customers for assigned branches.",
            [
                Permissions.StaffView, Permissions.StaffEdit,
                Permissions.VehiclesView, Permissions.VehiclesAssignStaff,
                Permissions.CustomersView, Permissions.CustomersRegister, Permissions.CustomersEdit,
                Permissions.TreksViewAll, Permissions.TreksViewDetails,
                Permissions.TreksCreate, Permissions.TreksEdit, Permissions.TreksAssign, Permissions.TreksStart,
                Permissions.TreksDownloadSheet, Permissions.TreksSendEmail,
                Permissions.TrekStopsView, Permissions.TrekStopsAdd, Permissions.TrekStopsEdit,
                Permissions.TrekStopsDelete, Permissions.TrekStopsReorder, Permissions.TrekStopsChangeCustomer,
                Permissions.TrekProductsView, Permissions.TrekProductsAdd, Permissions.TrekProductsEdit,
                Permissions.TrekProductsDelete,
                Permissions.TrekDeliveriesView,
                Permissions.ReportsView
            ]),

        ["FieldStaff"] = (
            "Assigned treks, customer registration and delivery capture.",
            [
                Permissions.CustomersView, Permissions.CustomersRegister, Permissions.CustomersEdit,
                Permissions.CustomerLocationsAdd, Permissions.CustomerLocationsEdit,
                Permissions.CustomerRepresentativesEdit,
                Permissions.CustomerPhotosUpload,
                Permissions.TreksViewAssigned, Permissions.TreksViewDetails,
                Permissions.TrekStopsView,
                Permissions.TrekDeliveriesRecord,
                Permissions.UnplannedSalesRecord,
                Permissions.TrekReturnsRecord
            ]),

        ["Driver"] = (
            "Assigned treks and delivery recording via driver portal or app.",
            [
                Permissions.TreksViewAssigned, Permissions.TreksViewDetails,
                Permissions.TrekStopsView,
                Permissions.TrekDeliveriesRecord,
                Permissions.UnplannedSalesRecord,
                Permissions.TrekReturnsRecord,
                Permissions.CustomersRegister,
                Permissions.CustomerLocationsAdd,
                Permissions.CustomerPhotosUpload
            ]),

        ["CreditOfficer"] = (
            "Customer KYC, credit assessment and ledger management.",
            [
                Permissions.CustomersView, Permissions.CustomersViewDetails,
                Permissions.CustomerKycView, Permissions.CustomerKycManage,
                Permissions.CustomerCreditView, Permissions.CustomerCreditManage,
                Permissions.LedgerView, Permissions.LedgerViewDetails,
                Permissions.LedgerCreateEntry, Permissions.LedgerEditEntry,
                Permissions.ReportsViewLedger, Permissions.ReportsExport
            ]),

        ["Auditor"] = (
            "Read-only access to reports and audit events.",
            [
                Permissions.CustomersView,
                Permissions.TreksViewAll,
                Permissions.VehiclesView,
                Permissions.TrackingViewAll,
                Permissions.ReportsView, Permissions.ReportsExport,
                Permissions.AuditView, Permissions.AuditExport
            ])
    };

    public static async Task SeedAsync(AppDbContext db)
    {
        var definedRoleNames = _roles.Keys.ToHashSet();

        // Remove system roles no longer in the definition
        var staleRoles = await db.Roles
            .Where(r => r.IsSystem && !definedRoleNames.Contains(r.Name))
            .ToListAsync();
        if (staleRoles.Count > 0)
            db.Roles.RemoveRange(staleRoles);

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
            else
            {
                role.Description = description;
            }

            var desiredPerms = permissions.ToHashSet();
            var existingPerms = role.RolePermissions.Select(rp => rp.Permission).ToHashSet();

            // Remove permissions no longer in the definition
            var toRemove = role.RolePermissions.Where(rp => !desiredPerms.Contains(rp.Permission)).ToList();
            if (toRemove.Count > 0)
                db.RolePermissions.RemoveRange(toRemove);

            // Add missing permissions
            foreach (var permission in desiredPerms.Except(existingPerms))
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
        }

        await db.SaveChangesAsync();
    }
}
