using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Organisation.Enums;

namespace prohpharmacy_trekking_app.Features.Organisation.Seeding;

public static class GhanaRegionSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (!await db.Regions.AnyAsync())
        {
            var json = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Data", "GhanaRegions.json"));

            var entries = JsonSerializer.Deserialize<List<RegionEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entries is not null)
            {
                foreach (var entry in entries)
                {
                    var region = new Region { Code = entry.Code, Name = entry.Region };
                    db.Regions.Add(region);
                    await db.SaveChangesAsync();

                    var districts = entry.Cities.Select((city, i) => new District
                    {
                        Code = $"{entry.Code}-{i + 1:D3}",
                        Name = city,
                        RegionId = region.Id
                    }).ToList();

                    db.Districts.AddRange(districts);
                    await db.SaveChangesAsync();
                }
            }
        }

        if (!await db.Branches.AnyAsync())
        {
            var hqRegion = await db.Regions.FirstOrDefaultAsync(r => r.Code == "GAC");
            var hqDistrict = hqRegion is not null
                ? await db.Districts.FirstOrDefaultAsync(d => d.RegionId == hqRegion.Id)
                : null;

            if (hqRegion is not null && hqDistrict is not null)
            {
                db.Branches.Add(new Branch
                {
                    Code = "HQ",
                    Name = "Head Office",
                    BranchType = BranchType.Retail,
                    RegionId = hqRegion.Id,
                    DistrictId = hqDistrict.Id,
                    Address = "Head Office, Accra, Ghana",
                    ContactNumber = "0000000000",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }
    }

    private class RegionEntry
    {
        public string Code { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public List<string> Cities { get; set; } = [];
    }
}
