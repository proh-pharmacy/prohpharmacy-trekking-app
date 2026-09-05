using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Identity.Entities;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;

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
        public DbSet<Locality> Localities => Set<Locality>();
        public DbSet<Branch> Branches => Set<Branch>();

        // Fleet
        public DbSet<Vehicle> Vehicles => Set<Vehicle>();
        public DbSet<TrackingDevice> TrackingDevices => Set<TrackingDevice>();
        public DbSet<VehicleStaffAssignment> VehicleStaffAssignments => Set<VehicleStaffAssignment>();
        public DbSet<StaffDeviceAssignment> StaffDeviceAssignments => Set<StaffDeviceAssignment>();

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
                entity.Property(r => r.Code).HasMaxLength(20).IsRequired();
                entity.Property(r => r.Name).HasMaxLength(120).IsRequired();
                entity.HasIndex(r => r.Code).IsUnique();
                entity.HasIndex(r => r.Name).IsUnique();
            });

            modelBuilder.Entity<District>(entity =>
            {
                entity.HasKey(d => d.Id);
                entity.Property(d => d.Code).HasMaxLength(20).IsRequired();
                entity.Property(d => d.Name).HasMaxLength(120).IsRequired();
                entity.HasOne(d => d.Region)
                    .WithMany(r => r.Districts)
                    .HasForeignKey(d => d.RegionId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(d => new { d.RegionId, d.Code }).IsUnique();
                entity.HasIndex(d => new { d.RegionId, d.Name }).IsUnique();
            });

            modelBuilder.Entity<Locality>(entity =>
            {
                entity.HasKey(l => l.Id);
                entity.Property(l => l.Code).HasMaxLength(20).IsRequired();
                entity.Property(l => l.Name).HasMaxLength(120).IsRequired();
                entity.HasOne(l => l.District)
                    .WithMany(d => d.Localities)
                    .HasForeignKey(l => l.DistrictId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(l => new { l.DistrictId, l.Code }).IsUnique();
                entity.HasIndex(l => new { l.DistrictId, l.Name }).IsUnique();
            });

            modelBuilder.Entity<Branch>(entity =>
            {
                entity.HasKey(b => b.Id);
                entity.Property(b => b.Code).HasMaxLength(30).IsRequired();
                entity.Property(b => b.Name).HasMaxLength(160).IsRequired();
                entity.Property(b => b.BranchType).HasConversion<string>().HasMaxLength(30).IsRequired();
                entity.Property(b => b.Address).HasMaxLength(300).IsRequired();
                entity.Property(b => b.Latitude).HasPrecision(9, 6);
                entity.Property(b => b.Longitude).HasPrecision(9, 6);
                entity.Property(b => b.ContactNumber).HasMaxLength(30).IsRequired();
                entity.HasOne(b => b.Region).WithMany(r => r.Branches).HasForeignKey(b => b.RegionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(b => b.District).WithMany(d => d.Branches).HasForeignKey(b => b.DistrictId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(b => b.Locality).WithMany(l => l.Branches).HasForeignKey(b => b.LocalityId).OnDelete(DeleteBehavior.Restrict);
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
                entity.HasIndex(d => d.TraccarUniqueId).IsUnique();
            });

            modelBuilder.Entity<StaffDeviceAssignment>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Notes).HasMaxLength(500);
                entity.HasOne(a => a.StaffMember)
                    .WithMany(s => s.DeviceAssignments)
                    .HasForeignKey(a => a.StaffMemberId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(a => a.Device)
                    .WithMany(d => d.Assignments)
                    .HasForeignKey(a => a.DeviceId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(a => new { a.DeviceId, a.UnassignedAt });
                entity.HasIndex(a => new { a.StaffMemberId, a.UnassignedAt });
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
                entity.HasOne(i => i.StaffMember)
                    .WithMany(s => s.Invitations)
                    .HasForeignKey(i => i.StaffMemberId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(i => i.Token).IsUnique();
            });
        }
    }
}
