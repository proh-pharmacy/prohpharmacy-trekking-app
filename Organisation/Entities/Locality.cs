namespace prohpharmacy_trekking_app.Organisation.Entities
{
    public class Locality
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid DistrictId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public District District { get; set; } = null!;
        public ICollection<Branch> Branches { get; set; } = [];
    }
}
