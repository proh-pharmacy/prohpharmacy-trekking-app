using Microsoft.EntityFrameworkCore;

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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Entity configurations go here
        }
    }
}
