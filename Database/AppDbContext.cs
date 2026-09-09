using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Features.Ledger.Entities;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Products.Entities;
using prohpharmacy_trekking_app.Features.Units.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Entities;

namespace prohpharmacy_trekking_app.Database
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // Organisation
        public DbSet<Region> Regions => Set<Region>();
        public DbSet<District> Districts => Set<District>();
        public DbSet<Branch> Branches => Set<Branch>();

        // Fleet
        public DbSet<Vehicle> Vehicles => Set<Vehicle>();
        public DbSet<TrackingDevice> TrackingDevices => Set<TrackingDevice>();
        public DbSet<VehicleStaffAssignment> VehicleStaffAssignments => Set<VehicleStaffAssignment>();
        public DbSet<FleetDriver> FleetDrivers => Set<FleetDriver>();

        // Customers
        public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
        public DbSet<CustomerPerson> CustomerPersons => Set<CustomerPerson>();
        public DbSet<CustomerLocation> CustomerLocations => Set<CustomerLocation>();

        // Products
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Unit> Units => Set<Unit>();

        // Ledger
        public DbSet<CustomerLedgerEntry> CustomerLedgerEntries => Set<CustomerLedgerEntry>();

        // Trekking
        public DbSet<TrekkingTrip> TrekkingTrips => Set<TrekkingTrip>();
        public DbSet<TrekkingTripStop> TrekkingTripStops => Set<TrekkingTripStop>();
        public DbSet<TrekkingTripStopProduct> TrekkingTripStopProducts => Set<TrekkingTripStopProduct>();

        // Staff
        public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

        // Identity
        public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── Organisation ──────────────────────────────────────────────────────

            modelBuilder.Entity<Region>(entity =>
            {
                entity.HasKey(r => r.Id);
                entity.Property(r => r.Code).HasMaxLength(100).IsRequired();
                entity.Property(r => r.Name).HasMaxLength(120).IsRequired();
                entity.HasIndex(r => r.Code).IsUnique();
                entity.HasIndex(r => r.Name).IsUnique();
            });

            modelBuilder.Entity<District>(entity =>
            {
                entity.HasKey(d => d.Id);
                entity.Property(d => d.Code).HasMaxLength(100).IsRequired();
                entity.Property(d => d.Name).HasMaxLength(120).IsRequired();
                entity.HasOne(d => d.Region)
                    .WithMany(r => r.Districts)
                    .HasForeignKey(d => d.RegionId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(d => new { d.RegionId, d.Code }).IsUnique();
                entity.HasIndex(d => new { d.RegionId, d.Name }).IsUnique();
            });

            modelBuilder.Entity<Branch>(entity =>
            {
                entity.HasKey(b => b.Id);
                entity.Property(b => b.Code).HasMaxLength(100).IsRequired();
                entity.Property(b => b.Name).HasMaxLength(160).IsRequired();
                entity.Property(b => b.BranchType).HasConversion<string>().HasMaxLength(30).IsRequired();
                entity.Property(b => b.Address).HasMaxLength(300).IsRequired();
                entity.Property(b => b.Latitude).HasPrecision(9, 6);
                entity.Property(b => b.Longitude).HasPrecision(9, 6);
                entity.Property(b => b.ContactNumber).HasMaxLength(30).IsRequired();
                entity.HasOne(b => b.Region).WithMany(r => r.Branches).HasForeignKey(b => b.RegionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(b => b.District).WithMany(d => d.Branches).HasForeignKey(b => b.DistrictId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(b => b.Code).IsUnique();
                entity.HasIndex(b => b.Name);
            });

            // ── Fleet ─────────────────────────────────────────────────────────────

            modelBuilder.Entity<Vehicle>(entity =>
            {
                entity.HasKey(v => v.Id);
                entity.Property(v => v.RegistrationNumber).HasMaxLength(30).IsRequired();
                entity.Property(v => v.DisplayName).HasMaxLength(80).IsRequired();
                entity.Property(v => v.Make).HasMaxLength(80).IsRequired();
                entity.Property(v => v.Model).HasMaxLength(80).IsRequired();
                entity.Property(v => v.Colour).HasMaxLength(50).IsRequired();
                entity.Property(v => v.OperationalStatus).HasConversion<string>().HasMaxLength(30).IsRequired();
                entity.HasOne(v => v.Branch)
                    .WithMany()
                    .HasForeignKey(v => v.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(v => v.RegistrationNumber).IsUnique();
            });

            modelBuilder.Entity<TrackingDevice>(entity =>
            {
                entity.HasKey(d => d.Id);
                entity.Property(d => d.TraccarUniqueId).HasMaxLength(100).IsRequired();
                entity.Property(d => d.Name).HasMaxLength(100).IsRequired();
                entity.Property(d => d.PhoneNumber).HasMaxLength(30);
                entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
                entity.Property(d => d.LastLatitude).HasPrecision(9, 6);
                entity.Property(d => d.LastLongitude).HasPrecision(9, 6);
                entity.HasOne(d => d.Vehicle)
                    .WithMany()
                    .HasForeignKey(d => d.VehicleId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(d => d.StaffMember)
                    .WithMany()
                    .HasForeignKey(d => d.StaffMemberId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasIndex(d => d.TraccarUniqueId).IsUnique();
                entity.HasIndex(d => d.VehicleId).IsUnique()
                    .HasFilter("\"VehicleId\" IS NOT NULL");
            });

            modelBuilder.Entity<FleetDriver>(entity =>
            {
                entity.HasKey(d => d.Id);
                entity.Property(d => d.TraccarUniqueId).HasMaxLength(100);
                entity.HasOne(d => d.StaffMember)
                    .WithMany()
                    .HasForeignKey(d => d.StaffMemberId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(d => d.StaffMemberId).IsUnique();
            });

            modelBuilder.Entity<VehicleStaffAssignment>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Notes).HasMaxLength(500);
                entity.HasOne(a => a.Vehicle)
                    .WithMany(v => v.StaffAssignments)
                    .HasForeignKey(a => a.VehicleId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.StaffMember)
                    .WithMany()
                    .HasForeignKey(a => a.StaffMemberId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(a => new { a.VehicleId, a.UnassignedAt });
            });

            // ── Customers ─────────────────────────────────────────────────────────

            modelBuilder.Entity<CustomerAccount>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.CustomerCode).HasMaxLength(20).IsRequired();
                entity.Property(a => a.BusinessName).HasMaxLength(200).IsRequired();
                entity.Property(a => a.TradingName).HasMaxLength(200);
                entity.Property(a => a.CustomerType).HasConversion<string>().HasMaxLength(40).IsRequired();
                entity.Property(a => a.PrimaryPhoneNumber).HasMaxLength(30).IsRequired();
                entity.Property(a => a.WhatsAppNumber).HasMaxLength(30);
                entity.Property(a => a.RegistrationStatus).HasConversion<string>().HasMaxLength(30).IsRequired();
                entity.HasOne(a => a.Region).WithMany().HasForeignKey(a => a.RegionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.OwningBranch).WithMany().HasForeignKey(a => a.OwningBranchId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.RegisteredBy).WithMany().HasForeignKey(a => a.RegisteredByStaffId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(a => a.CustomerCode).IsUnique();
                entity.HasIndex(a => a.ClientGeneratedId).IsUnique()
                    .HasFilter("\"ClientGeneratedId\" IS NOT NULL");
                entity.HasIndex(a => a.BusinessName);
                entity.HasIndex(a => a.PrimaryPhoneNumber);
            });

            modelBuilder.Entity<CustomerPerson>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.FirstName).HasMaxLength(80).IsRequired();
                entity.Property(p => p.MiddleName).HasMaxLength(80);
                entity.Property(p => p.LastName).HasMaxLength(80).IsRequired();
                entity.Property(p => p.RelationshipType).HasConversion<string>().HasMaxLength(40).IsRequired();
                entity.Property(p => p.PrimaryPhoneNumber).HasMaxLength(30).IsRequired();
                entity.Property(p => p.AlternativePhoneNumber).HasMaxLength(30);
                entity.Property(p => p.EmailAddress).HasMaxLength(200);
                entity.Property(p => p.GhanaCardNumber).HasMaxLength(30);
                entity.HasOne(p => p.CustomerAccount)
                    .WithMany(a => a.People)
                    .HasForeignKey(p => p.CustomerAccountId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CustomerLocation>(entity =>
            {
                entity.HasKey(l => l.Id);
                entity.Property(l => l.LocationType).HasConversion<string>().HasMaxLength(30).IsRequired();
                entity.Property(l => l.StreetAddress).HasMaxLength(300);
                entity.Property(l => l.LandmarkAndDirections).HasMaxLength(500);
                entity.Property(l => l.Latitude).HasPrecision(9, 6);
                entity.Property(l => l.Longitude).HasPrecision(9, 6);
                entity.Property(l => l.AccuracyMetres).HasPrecision(8, 2);
                entity.Property(l => l.CaptureMethod).HasConversion<string>().HasMaxLength(30).IsRequired();
                entity.Property(l => l.VerificationStatus).HasConversion<string>().HasMaxLength(40).IsRequired();
                entity.HasOne(l => l.CustomerAccount)
                    .WithMany(a => a.Locations)
                    .HasForeignKey(l => l.CustomerAccountId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(l => l.Region).WithMany().HasForeignKey(l => l.RegionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(l => l.District).WithMany().HasForeignKey(l => l.DistrictId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(l => l.CapturedBy).WithMany().HasForeignKey(l => l.CapturedByStaffId).OnDelete(DeleteBehavior.Restrict);
            });

            // ── Products ──────────────────────────────────────────────────────────

            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
                entity.Property(p => p.Unit).HasMaxLength(80);
                entity.Property(p => p.Description).HasMaxLength(500);
            });

            modelBuilder.Entity<Unit>(entity =>
            {
                entity.HasKey(u => u.Id);
                entity.Property(u => u.Name).HasMaxLength(80).IsRequired();
                entity.HasIndex(u => u.Name).IsUnique();
            });

            // ── Ledger ────────────────────────────────────────────────────────────

            modelBuilder.Entity<CustomerLedgerEntry>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EntryType).HasConversion<string>().HasMaxLength(10).IsRequired();
                entity.Property(e => e.Amount).HasPrecision(14, 2).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(500).IsRequired();
                entity.Property(e => e.PaymentMethod).HasMaxLength(30);
                entity.HasOne(e => e.CustomerAccount)
                    .WithMany()
                    .HasForeignKey(e => e.CustomerAccountId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.TrekkingTrip)
                    .WithMany()
                    .HasForeignKey(e => e.TrekkingTripId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(e => e.TrekkingTripStop)
                    .WithMany()
                    .HasForeignKey(e => e.TrekkingTripStopId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(e => e.CreatedBy)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedByStaffId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(e => e.CustomerAccountId);
                entity.HasIndex(e => new { e.TrekkingTripStopId, e.IsAutoGenerated });
                entity.HasIndex(e => e.RecordedAt);
            });

            // ── Trekking ──────────────────────────────────────────────────────────

            modelBuilder.Entity<TrekkingTrip>(entity =>
            {
                entity.HasKey(t => t.Id);
                entity.Property(t => t.TrekNumber).HasMaxLength(20).IsRequired();
                entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
                entity.Property(t => t.Notes).HasMaxLength(500);
                entity.HasOne(t => t.Branch)
                    .WithMany()
                    .HasForeignKey(t => t.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(t => t.Driver)
                    .WithMany()
                    .HasForeignKey(t => t.DriverStaffId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(t => t.Vehicle)
                    .WithMany()
                    .HasForeignKey(t => t.VehicleId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(t => t.TrekNumber).IsUnique();
                entity.HasIndex(t => t.DriverToken).IsUnique()
                    .HasFilter("\"DriverToken\" IS NOT NULL");
            });

            modelBuilder.Entity<TrekkingTripStop>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Notes).HasMaxLength(500);
                entity.HasOne(s => s.TrekkingTrip)
                    .WithMany(t => t.Stops)
                    .HasForeignKey(s => s.TrekkingTripId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(s => s.CustomerAccount)
                    .WithMany()
                    .HasForeignKey(s => s.CustomerAccountId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(s => new { s.TrekkingTripId, s.Sequence }).IsUnique();
            });

            modelBuilder.Entity<TrekkingTripStopProduct>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.PlannedQuantity).HasPrecision(10, 3);
                entity.Property(p => p.QtyDelivered).HasPrecision(10, 3);
                entity.Property(p => p.AmtPaid).HasPrecision(14, 2);
                entity.Property(p => p.Balance).HasPrecision(14, 2);
                entity.Property(p => p.PaymentMethod).HasConversion<string>().HasMaxLength(30);
                entity.Property(p => p.Notes).HasMaxLength(500);
                entity.HasOne(p => p.TrekkingTripStop)
                    .WithMany(s => s.Products)
                    .HasForeignKey(p => p.TrekkingTripStopId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(p => p.Product)
                    .WithMany()
                    .HasForeignKey(p => p.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ── Staff ─────────────────────────────────────────────────────────────

            modelBuilder.Entity<StaffMember>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.EmployeeNumber).HasMaxLength(30);
                entity.Property(s => s.FirstName).HasMaxLength(80).IsRequired();
                entity.Property(s => s.LastName).HasMaxLength(80).IsRequired();
                entity.Property(s => s.PhoneNumber).HasMaxLength(30).IsRequired();
                entity.Property(s => s.EmailAddress).HasMaxLength(200).IsRequired();
                entity.Property(s => s.Role).HasMaxLength(60);
                entity.Property(s => s.EmploymentStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
                entity.Property(s => s.ProfilePhotoObjectKey).HasMaxLength(500);
                entity.UseXminAsConcurrencyToken();
                entity.HasOne(s => s.Branch)
                    .WithMany()
                    .HasForeignKey(s => s.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(s => s.EmployeeNumber).IsUnique()
                    .HasFilter("\"EmployeeNumber\" IS NOT NULL");
                entity.HasIndex(s => s.EmailAddress).IsUnique();
            });

            // ── Identity ──────────────────────────────────────────────────────────

            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.HasKey(u => u.Id);
                entity.Property(u => u.Email).HasMaxLength(200).IsRequired();
                entity.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
                entity.HasOne(u => u.StaffMember)
                    .WithOne(s => s.ApplicationUser)
                    .HasForeignKey<ApplicationUser>(u => u.StaffMemberId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(u => u.Email).IsUnique();
                entity.HasIndex(u => u.StaffMemberId).IsUnique();
            });

            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasKey(r => r.Id);
                entity.Property(r => r.Name).HasMaxLength(60).IsRequired();
                entity.Property(r => r.Description).HasMaxLength(300);
                entity.HasIndex(r => r.Name).IsUnique();
            });

            modelBuilder.Entity<RolePermission>(entity =>
            {
                entity.HasKey(rp => new { rp.RoleId, rp.Permission });
                entity.Property(rp => rp.Permission).HasMaxLength(80).IsRequired();
                entity.HasOne(rp => rp.Role)
                    .WithMany(r => r.RolePermissions)
                    .HasForeignKey(rp => rp.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.HasKey(ur => new { ur.UserId, ur.RoleId });
                entity.HasOne(ur => ur.User)
                    .WithMany(u => u.UserRoles)
                    .HasForeignKey(ur => ur.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(ur => ur.Role)
                    .WithMany(r => r.UserRoles)
                    .HasForeignKey(ur => ur.RoleId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasKey(rt => rt.Id);
                entity.Property(rt => rt.Token).HasMaxLength(256).IsRequired();
                entity.Property(rt => rt.RevokedReason).HasMaxLength(300);
                entity.HasOne(rt => rt.User)
                    .WithMany(u => u.RefreshTokens)
                    .HasForeignKey(rt => rt.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(rt => rt.Token).IsUnique();
            });

            modelBuilder.Entity<StaffInvitation>(entity =>
            {
                entity.HasKey(i => i.Id);
                entity.Property(i => i.Token).HasMaxLength(256).IsRequired();
                entity.Property(i => i.Roles).HasColumnType("text[]").IsRequired();
                entity.HasOne(i => i.StaffMember)
                    .WithMany(s => s.Invitations)
                    .HasForeignKey(i => i.StaffMemberId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(i => i.Token).IsUnique();
            });
        }
    }
}
