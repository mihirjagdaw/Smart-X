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
    }
}
