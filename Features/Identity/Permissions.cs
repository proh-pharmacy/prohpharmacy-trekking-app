namespace prohpharmacy_trekking_app.Features.Identity;

public static class Permissions
{
    public const string StaffView = "Staff.View";
    public const string StaffManage = "Staff.Manage";
    public const string RolesManage = "Roles.Manage";
    public const string BranchesManage = "Branches.Manage";
    public const string VehiclesManage = "Vehicles.Manage";
    public const string TrackingDevicesManage = "TrackingDevices.Manage";
    public const string TreksViewAll = "Treks.ViewAll";
    public const string TreksCreate = "Treks.Create";
    public const string TreksAssign = "Treks.Assign";
    public const string TreksStart = "Treks.Start";
    public const string TreksComplete = "Treks.Complete";
    public const string CustomersRegister = "Customers.Register";
    public const string CustomersEdit = "Customers.Edit";
    public const string CustomersApprove = "Customers.Approve";
    public const string CustomerKycView = "CustomerKyc.View";
    public const string CustomerKycManage = "CustomerKyc.Manage";
    public const string CustomerCreditView = "CustomerCredit.View";
    public const string CustomerCreditManage = "CustomerCredit.Manage";
    public const string VisitsRecord = "Visits.Record";
    public const string VisitsVerify = "Visits.Verify";
    public const string TrackingViewAll = "Tracking.ViewAll";
    public const string ReportsExport = "Reports.Export";
    public const string AuditView = "Audit.View";

    public static readonly string[] All =
    [
        StaffView, StaffManage, RolesManage, BranchesManage, VehiclesManage,
        TrackingDevicesManage, TreksViewAll, TreksCreate, TreksAssign, TreksStart,
        TreksComplete, CustomersRegister, CustomersEdit, CustomersApprove,
        CustomerKycView, CustomerKycManage, CustomerCreditView, CustomerCreditManage,
        VisitsRecord, VisitsVerify, TrackingViewAll, ReportsExport, AuditView
    ];
}
