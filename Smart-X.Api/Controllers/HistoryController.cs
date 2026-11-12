using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Data;
using Smart_X.Api.History;
using Smart_X.Api.Processing;
using Smart_X.Shared;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HistoryController : ControllerBase
    {
        private const int MaxPage = 1000;
        private const int MaxSeedReadings = 300_000;
        private const int SaveBatch = 5000;

        private readonly TelemetryArchive _archive;
        private readonly DeviceRegistry _registry;
        private readonly AppDbContext _db;

        public HistoryController(TelemetryArchive archive, DeviceRegistry registry, AppDbContext db)
        {
            _archive = archive;
            _registry = registry;
            _db = db;
        }

        /// <summary>
        /// A device's stored readings between from and to (both included, either optional), one page at a time.
        /// Write times as ISO 8601 with a Z (2026-11-13T14:00:00Z); a + in the query string must be escaped.
        /// </summary>
        [HttpGet("{mac}")]
        public IActionResult Get(string mac, DateTimeOffset? from, DateTimeOffset? to, int skip = 0, int take = 100, bool newestFirst = true)
        {
            if (!_registry.TryGet(mac, out var device))
            {
                return NotFound($"Device {mac} is not registered.");
            }

            if (skip < 0 || take < 1 || take > MaxPage)
            {
                return BadRequest($"skip must be 0 or more and take between 1 and {MaxPage}.");
            }

            if (from > to)
            {
                return BadRequest("from must not be after to.");
            }

            var page = _archive.Query(device.Mac, from ?? DateTimeOffset.MinValue, to ?? DateTimeOffset.MaxValue, skip, take, newestFirst);

            return Ok(new HistoryPageResponse(
                device.Mac, device.Category, from, to, page.Total, skip, take, newestFirst,
                page.Items.Select(r => new HistoryReading(r.Timestamp, r.Value)).ToList(),
                new HistoryStats(page.TreeSize, page.TreeHeight, page.ElapsedMicroseconds)));
        }

        /// <summary>
        /// Testing hook: backfills a device with a believable history ending now (a daily cycle plus noise), so
        /// there is a large history to query. It is written to the database and to the archive.
        /// </summary>
        [HttpPost("{mac}/seed")]
        public async Task<IActionResult> Seed(string mac, int days = 7, int everySeconds = 60)
        {
            if (!_registry.TryGet(mac, out var device))
            {
                return NotFound($"Device {mac} is not registered.");
            }

            if (days < 1 || days > 90 || everySeconds < 1 || everySeconds > 3600)
            {
                return BadRequest("days must be 1 to 90 and everySeconds 1 to 3600.");
            }

            var count = (int)((long)days * 86_400 / everySeconds);
            if (count > MaxSeedReadings)
            {
                return BadRequest($"That would add {count:N0} readings; the limit is {MaxSeedReadings:N0}. Use fewer days or a longer interval.");
            }

            var started = Stopwatch.GetTimestamp();
            var end = DateTimeOffset.UtcNow;
            var begin = end - TimeSpan.FromDays(days);
            var random = new Random(device.Mac.GetHashCode());

            _db.ChangeTracker.AutoDetectChangesEnabled = false;
            for (var written = 0; written < count; written += SaveBatch)
            {
                for (var i = written; i < Math.Min(written + SaveBatch, count); i++)
                {
                    var at = begin + TimeSpan.FromSeconds((double)i * everySeconds);
                    var value = SeedValue(device.Category, at, random);
                    _db.TelemetryRecords.Add(new TelemetryRecord(0, device.Mac, device.Category, value, at));
                    _archive.Add(device.Mac, at, value);
                }

                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
            }

            var elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            return Ok(new HistorySeedResponse(device.Mac, count, begin, end, elapsed));
        }

        // A day-long cycle (warmest mid-afternoon UTC) plus noise, shaped for each kind of sensor.
        private static float SeedValue(SensorCategory category, DateTimeOffset at, Random random)
        {
            var hours = at.UtcDateTime.TimeOfDay.TotalHours;
            var cycle = Math.Cos((hours - 15) / 24 * 2 * Math.PI);

            return category switch
            {
                SensorCategory.Environmental => (float)Math.Clamp(45 + 12 * cycle + random.NextDouble() * 4 - 2, 0, 100),
                SensorCategory.PowerConsumption => (float)Math.Round(1200 + 500 * cycle + random.NextDouble() * 80 - 40),
                _ => hours is >= 6 and < 9 ? 1f : 0f
            };
        }
    }
}
