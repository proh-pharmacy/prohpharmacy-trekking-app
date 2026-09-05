using prohpharmacy_trekking_app.Organisation.Enums;

namespace prohpharmacy_trekking_app.Organisation.Entities
{
    public class Branch
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public BranchType BranchType { get; set; }
        public Guid RegionId { get; set; }
        public Guid DistrictId { get; set; }
        public Guid LocalityId { get; set; }
        public string Address { get; set; } = string.Empty;
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public string ContactNumber { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Region Region { get; set; } = null!;
        public District District { get; set; } = null!;
        public Locality Locality { get; set; } = null!;
    }
}
