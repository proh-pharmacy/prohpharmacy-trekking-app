namespace prohpharmacy_trekking_app.Features.Organisation.Entities
{
    public class District
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid RegionId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public Region Region { get; set; } = null!;
        public ICollection<Locality> Localities { get; set; } = [];
        public ICollection<Branch> Branches { get; set; } = [];
    }
}
