namespace prohpharmacy_trekking_app.Features.Identity;

public static class Permissions
{
    // Users
    public const string UsersView = "Users.View";
    public const string UsersViewDetails = "Users.ViewDetails";
    public const string UsersInvite = "Users.Invite";
    public const string UsersResendInvitation = "Users.ResendInvitation";
    public const string UsersActivate = "Users.Activate";
    public const string UsersSuspend = "Users.Suspend";
    public const string UsersRevokeSessions = "Users.RevokeSessions";
    public const string UsersResetPassword = "Users.ResetPassword";
    public const string UsersAssignRoles = "Users.AssignRoles";
    public const string UsersRemoveRoles = "Users.RemoveRoles";
    public const string UsersEdit = "Users.Edit";

    // Staff
    public const string StaffView = "Staff.View";
    public const string StaffViewDetails = "Staff.ViewDetails";
    public const string StaffCreate = "Staff.Create";
    public const string StaffEdit = "Staff.Edit";
    public const string StaffChangeStatus = "Staff.ChangeStatus";
    public const string StaffGrantAccess = "Staff.GrantAccess";
    public const string StaffUploadPhoto = "Staff.UploadPhoto";

    // Roles
    public const string RolesView = "Roles.View";
    public const string RolesCreate = "Roles.Create";
    public const string RolesEdit = "Roles.Edit";
    public const string RolesManagePermissions = "Roles.ManagePermissions";
    public const string RolesDelete = "Roles.Delete";

    // Organisation — Regions
    public const string RegionsView = "Regions.View";
    public const string RegionsCreate = "Regions.Create";
    public const string RegionsEdit = "Regions.Edit";

    // Organisation — Districts
    public const string DistrictsView = "Districts.View";
    public const string DistrictsCreate = "Districts.Create";
    public const string DistrictsEdit = "Districts.Edit";

    // Organisation — Branches
    public const string BranchesView = "Branches.View";
    public const string BranchesViewDetails = "Branches.ViewDetails";
    public const string BranchesCreate = "Branches.Create";
    public const string BranchesEdit = "Branches.Edit";
    public const string BranchesChangeStatus = "Branches.ChangeStatus";

    // Products
    public const string ProductsView = "Products.View";
    public const string ProductsViewDetails = "Products.ViewDetails";
    public const string ProductsCreate = "Products.Create";
    public const string ProductsEdit = "Products.Edit";
    public const string ProductsChangeStatus = "Products.ChangeStatus";
    public const string ProductsImport = "Products.Import";
    public const string ProductsExport = "Products.Export";

    // Units
    public const string UnitsView = "Units.View";
    public const string UnitsCreate = "Units.Create";
    public const string UnitsEdit = "Units.Edit";
    public const string UnitsChangeStatus = "Units.ChangeStatus";

    // Customers
    public const string CustomersView = "Customers.View";
    public const string CustomersViewDetails = "Customers.ViewDetails";
    public const string CustomersRegister = "Customers.Register";
    public const string CustomersEdit = "Customers.Edit";
    public const string CustomersApprove = "Customers.Approve";
    public const string CustomersExport = "Customers.Export";
    public const string CustomersMapView = "Customers.MapView";

    // Customer Locations
    public const string CustomerLocationsView = "CustomerLocations.View";
    public const string CustomerLocationsAdd = "CustomerLocations.Add";
    public const string CustomerLocationsEdit = "CustomerLocations.Edit";
    public const string CustomerLocationsDelete = "CustomerLocations.Delete";
    public const string CustomerLocationsSetPrimary = "CustomerLocations.SetPrimary";

    // Customer Representatives
    public const string CustomerRepresentativesView = "CustomerRepresentatives.View";
    public const string CustomerRepresentativesEdit = "CustomerRepresentatives.Edit";

    // Customer Photos
    public const string CustomerPhotosView = "CustomerPhotos.View";
    public const string CustomerPhotosUpload = "CustomerPhotos.Upload";
    public const string CustomerPhotosReplace = "CustomerPhotos.Replace";

    // Customer KYC
    public const string CustomerKycView = "CustomerKyc.View";
    public const string CustomerKycManage = "CustomerKyc.Manage";

    // Customer Credit
    public const string CustomerCreditView = "CustomerCredit.View";
    public const string CustomerCreditManage = "CustomerCredit.Manage";

    // Ledger
    public const string LedgerView = "Ledger.View";
    public const string LedgerViewDetails = "Ledger.ViewDetails";
    public const string LedgerCreateEntry = "Ledger.CreateEntry";
    public const string LedgerEditEntry = "Ledger.EditEntry";
    public const string LedgerDeleteEntry = "Ledger.DeleteEntry";
    public const string LedgerExport = "Ledger.Export";

    // Treks
    public const string TreksViewAll = "Treks.ViewAll";
    public const string TreksViewAssigned = "Treks.ViewAssigned";
    public const string TreksViewDetails = "Treks.ViewDetails";
    public const string TreksCreate = "Treks.Create";
    public const string TreksEdit = "Treks.Edit";
    public const string TreksDelete = "Treks.Delete";
    public const string TreksAssign = "Treks.Assign";
    public const string TreksStart = "Treks.Start";
    public const string TreksComplete = "Treks.Complete";
    public const string TreksCancel = "Treks.Cancel";
    public const string TreksChangeStatus = "Treks.ChangeStatus";
    public const string TreksExport = "Treks.Export";
    public const string TreksDownloadSheet = "Treks.DownloadSheet";
    public const string TreksGenerateDriverLink = "Treks.GenerateDriverLink";
    public const string TreksSendEmail = "Treks.SendEmail";

    // Trek Stops
    public const string TrekStopsView = "TrekStops.View";
    public const string TrekStopsAdd = "TrekStops.Add";
    public const string TrekStopsEdit = "TrekStops.Edit";
    public const string TrekStopsDelete = "TrekStops.Delete";
    public const string TrekStopsReorder = "TrekStops.Reorder";
    public const string TrekStopsChangeCustomer = "TrekStops.ChangeCustomer";

    // Trek Products (planned)
    public const string TrekProductsView = "TrekProducts.View";
    public const string TrekProductsAdd = "TrekProducts.Add";
    public const string TrekProductsEdit = "TrekProducts.Edit";
    public const string TrekProductsDelete = "TrekProducts.Delete";
    public const string TrekProductsOverridePrice = "TrekProducts.OverridePrice";

    // Trek Deliveries
    public const string TrekDeliveriesView = "TrekDeliveries.View";
    public const string TrekDeliveriesRecord = "TrekDeliveries.Record";
    public const string TrekDeliveriesEdit = "TrekDeliveries.Edit";

    // Unplanned Sales
    public const string UnplannedSalesView = "UnplannedSales.View";
    public const string UnplannedSalesRecord = "UnplannedSales.Record";
    public const string UnplannedSalesEdit = "UnplannedSales.Edit";
    public const string UnplannedSalesDelete = "UnplannedSales.Delete";

    // Trek Returns
    public const string TrekReturnsView = "TrekReturns.View";
    public const string TrekReturnsRecord = "TrekReturns.Record";
    public const string TrekReturnsDelete = "TrekReturns.Delete";

    // Trek Pricing / Catalogue Sync
    public const string TrekPricingViewDiff = "TrekPricing.ViewDiff";
    public const string TrekPricingSync = "TrekPricing.Sync";

    // Fleet — Vehicles
    public const string VehiclesView = "Vehicles.View";
    public const string VehiclesViewDetails = "Vehicles.ViewDetails";
    public const string VehiclesCreate = "Vehicles.Create";
    public const string VehiclesEdit = "Vehicles.Edit";
    public const string VehiclesChangeStatus = "Vehicles.ChangeStatus";
    public const string VehiclesAssignStaff = "Vehicles.AssignStaff";
    public const string VehiclesUnassignStaff = "Vehicles.UnassignStaff";

    // Fleet — Tracking Devices
    public const string TrackingDevicesView = "TrackingDevices.View";
    public const string TrackingDevicesViewDetails = "TrackingDevices.ViewDetails";
    public const string TrackingDevicesCreate = "TrackingDevices.Create";
    public const string TrackingDevicesEdit = "TrackingDevices.Edit";
    public const string TrackingDevicesDelete = "TrackingDevices.Delete";
    public const string TrackingDevicesSync = "TrackingDevices.Sync";

    // Fleet — Fleet Drivers
    public const string FleetDriversView = "FleetDrivers.View";
    public const string FleetDriversRegister = "FleetDrivers.Register";
    public const string FleetDriversRemove = "FleetDrivers.Remove";
    public const string FleetDriversSync = "FleetDrivers.Sync";

    // Fleet — Traccar Users
    public const string TraccarUsersView = "TraccarUsers.View";
    public const string TraccarUsersCreate = "TraccarUsers.Create";
    public const string TraccarUsersEdit = "TraccarUsers.Edit";
    public const string TraccarUsersDelete = "TraccarUsers.Delete";
    public const string TraccarUsersSync = "TraccarUsers.Sync";

    // Live Tracking
    public const string TrackingViewAll = "Tracking.ViewAll";
    public const string TrackingViewAssigned = "Tracking.ViewAssigned";
    public const string TrackingViewLive = "Tracking.ViewLive";
    public const string TrackingViewHistory = "Tracking.ViewHistory";
    public const string TrackingExport = "Tracking.Export";

    // Reports
    public const string ReportsView = "Reports.View";
    public const string ReportsViewCollections = "Reports.ViewCollections";
    public const string ReportsViewLedger = "Reports.ViewLedger";
    public const string ReportsViewProducts = "Reports.ViewProducts";
    public const string ReportsViewTreks = "Reports.ViewTreks";
    public const string ReportsExport = "Reports.Export";

    // Audit
    public const string AuditView = "Audit.View";
    public const string AuditExport = "Audit.Export";

    public static readonly string[] All =
    [
        UsersView, UsersViewDetails, UsersInvite, UsersResendInvitation, UsersActivate,
        UsersSuspend, UsersRevokeSessions, UsersResetPassword, UsersAssignRoles, UsersRemoveRoles, UsersEdit,

        StaffView, StaffViewDetails, StaffCreate, StaffEdit, StaffChangeStatus, StaffGrantAccess, StaffUploadPhoto,

        RolesView, RolesCreate, RolesEdit, RolesManagePermissions, RolesDelete,

        RegionsView, RegionsCreate, RegionsEdit,
        DistrictsView, DistrictsCreate, DistrictsEdit,
        BranchesView, BranchesViewDetails, BranchesCreate, BranchesEdit, BranchesChangeStatus,

        ProductsView, ProductsViewDetails, ProductsCreate, ProductsEdit, ProductsChangeStatus, ProductsImport, ProductsExport,
        UnitsView, UnitsCreate, UnitsEdit, UnitsChangeStatus,

        CustomersView, CustomersViewDetails, CustomersRegister, CustomersEdit, CustomersApprove, CustomersExport, CustomersMapView,
        CustomerLocationsView, CustomerLocationsAdd, CustomerLocationsEdit, CustomerLocationsDelete, CustomerLocationsSetPrimary,
        CustomerRepresentativesView, CustomerRepresentativesEdit,
        CustomerPhotosView, CustomerPhotosUpload, CustomerPhotosReplace,
        CustomerKycView, CustomerKycManage,
        CustomerCreditView, CustomerCreditManage,

        LedgerView, LedgerViewDetails, LedgerCreateEntry, LedgerEditEntry, LedgerDeleteEntry, LedgerExport,

        TreksViewAll, TreksViewAssigned, TreksViewDetails, TreksCreate, TreksEdit, TreksDelete,
        TreksAssign, TreksStart, TreksComplete, TreksCancel, TreksChangeStatus,
        TreksExport, TreksDownloadSheet, TreksGenerateDriverLink, TreksSendEmail,

        TrekStopsView, TrekStopsAdd, TrekStopsEdit, TrekStopsDelete, TrekStopsReorder, TrekStopsChangeCustomer,

        TrekProductsView, TrekProductsAdd, TrekProductsEdit, TrekProductsDelete, TrekProductsOverridePrice,

        TrekDeliveriesView, TrekDeliveriesRecord, TrekDeliveriesEdit,

        UnplannedSalesView, UnplannedSalesRecord, UnplannedSalesEdit, UnplannedSalesDelete,

        TrekReturnsView, TrekReturnsRecord, TrekReturnsDelete,

        TrekPricingViewDiff, TrekPricingSync,

        VehiclesView, VehiclesViewDetails, VehiclesCreate, VehiclesEdit, VehiclesChangeStatus, VehiclesAssignStaff, VehiclesUnassignStaff,

        TrackingDevicesView, TrackingDevicesViewDetails, TrackingDevicesCreate, TrackingDevicesEdit, TrackingDevicesDelete, TrackingDevicesSync,

        FleetDriversView, FleetDriversRegister, FleetDriversRemove, FleetDriversSync,

        TraccarUsersView, TraccarUsersCreate, TraccarUsersEdit, TraccarUsersDelete, TraccarUsersSync,

        TrackingViewAll, TrackingViewAssigned, TrackingViewLive, TrackingViewHistory, TrackingExport,

        ReportsView, ReportsViewCollections, ReportsViewLedger, ReportsViewProducts, ReportsViewTreks, ReportsExport,

        AuditView, AuditExport
    ];
}
