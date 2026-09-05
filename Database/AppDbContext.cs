using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Organisation.Entities;

namespace prohpharmacy_trekking_app.Database
{
    /// <summary>
    /// Main application DbContext.
    /// Add DbSet&lt;YourEntity&gt; properties here as you create domain entities.
    /// </summary>
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Region> Regions => Set<Region>();
        public DbSet<District> Districts => Set<District>();
        public DbSet<Locality> Localities => Set<Locality>();
        public DbSet<Branch> Branches => Set<Branch>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Region>(entity =>
            {
                entity.HasKey(region => region.Id);

                entity.Property(region => region.Code)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(region => region.Name)
                    .HasMaxLength(120)
                    .IsRequired();

                entity.HasIndex(region => region.Code)
                    .IsUnique();

                entity.HasIndex(region => region.Name)
                    .IsUnique();
            });

            modelBuilder.Entity<District>(entity =>
            {
                entity.HasKey(district => district.Id);

                entity.Property(district => district.Code)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(district => district.Name)
                    .HasMaxLength(120)
                    .IsRequired();

                entity.HasOne(district => district.Region)
                    .WithMany(region => region.Districts)
                    .HasForeignKey(district => district.RegionId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(district => new { district.RegionId, district.Code })
                    .IsUnique();

                entity.HasIndex(district => new { district.RegionId, district.Name })
                    .IsUnique();
            });

            modelBuilder.Entity<Locality>(entity =>
            {
                entity.HasKey(locality => locality.Id);

                entity.Property(locality => locality.Code)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(locality => locality.Name)
                    .HasMaxLength(120)
                    .IsRequired();

                entity.HasOne(locality => locality.District)
                    .WithMany(district => district.Localities)
                    .HasForeignKey(locality => locality.DistrictId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(locality => new { locality.DistrictId, locality.Code })
                    .IsUnique();

                entity.HasIndex(locality => new { locality.DistrictId, locality.Name })
                    .IsUnique();
            });

            modelBuilder.Entity<Branch>(entity =>
            {
                entity.HasKey(branch => branch.Id);

                entity.Property(branch => branch.Code)
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(branch => branch.Name)
                    .HasMaxLength(160)
                    .IsRequired();

                entity.Property(branch => branch.BranchType)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(branch => branch.Address)
                    .HasMaxLength(300)
                    .IsRequired();

                entity.Property(branch => branch.Latitude)
                    .HasPrecision(9, 6);

                entity.Property(branch => branch.Longitude)
                    .HasPrecision(9, 6);

                entity.Property(branch => branch.ContactNumber)
                    .HasMaxLength(30)
                    .IsRequired();

                entity.HasOne(branch => branch.Region)
                    .WithMany(region => region.Branches)
                    .HasForeignKey(branch => branch.RegionId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(branch => branch.District)
                    .WithMany(district => district.Branches)
                    .HasForeignKey(branch => branch.DistrictId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(branch => branch.Locality)
                    .WithMany(locality => locality.Branches)
                    .HasForeignKey(branch => branch.LocalityId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(branch => branch.Code)
                    .IsUnique();

                entity.HasIndex(branch => branch.Name);
            });
        }
    }
}
