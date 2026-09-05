namespace prohpharmacy_trekking_app.Organisation.Entities
{
    public class Region
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<District> Districts { get; set; } = [];
        public ICollection<Branch> Branches { get; set; } = [];
    }
}
