using Microsoft.EntityFrameworkCore;

namespace Smart_X.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {

        }

        public DbSet<SensorProfile> Sensors => Set<SensorProfile>();
        public DbSet<TelemetryRecord> TelemetryRecords => Set<TelemetryRecord>();
        public DbSet<SensorFile> SensorFiles => Set<SensorFile>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Lets the database delete one device's history, or everything older than a cutoff, without reading it all.
            modelBuilder.Entity<TelemetryRecord>().HasIndex(r => new { r.DeviceId, r.Timestamp });
        }
    }
}
