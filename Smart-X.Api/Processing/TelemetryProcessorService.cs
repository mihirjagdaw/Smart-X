using Smart_X.Api.Data;
using Smart_X.Shared;

namespace Smart_X.Api.Processing
{
    /// <summary>
    /// Single consumer for the ingest pipeline: looks the device up in the registry, stores the
    /// reading in memory and in the database, then updates the device's last-seen state.
    /// </summary>
    public class TelemetryProcessorService : BackgroundService
    {
        private readonly TelemetryPipeline _pipeline;
        private readonly DeviceRegistry _registry;
        private readonly TelemetryHistoryStore _store;
        private readonly AlertTracker _alerts;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TelemetryProcessorService> _logger;
        private readonly TimeSpan _standardDelay;

        public TelemetryProcessorService(
            TelemetryPipeline pipeline,
            DeviceRegistry registry,
            TelemetryHistoryStore store,
            AlertTracker alerts,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<TelemetryProcessorService> logger)
        {
            _pipeline = pipeline;
            _registry = registry;
            _store = store;
            _alerts = alerts;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _standardDelay = TimeSpan.FromMilliseconds(configuration.GetValue("Processing:StandardDelayMs", 100));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var (packet, lane) = await _pipeline.DequeueAsync(stoppingToken);

                try
                {
                    await Process(packet, lane);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process packet for {Mac}", packet.Mac);
                }

                // shortcut: serial consumer with one DB write per packet; the delay only throttles the
                // standard lane so a backlog is visible and critical packets overtake it. Set
                // Processing:StandardDelayMs=0 and batch the writes if real throughput is ever needed.
                if (lane == Lane.Standard && _standardDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_standardDelay, stoppingToken);
                }
            }
        }

        private async Task Process(Packet packet, Lane lane)
        {
            if (!_registry.TryGet(packet.Mac, out var device) || !device.IsActive)
            {
                _pipeline.RecordDropped();
                return;
            }

            _store.Add(new SensorReading(packet.Mac, packet.Category, packet.Value, packet.Timestamp));

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TelemetryRecords.Add(new TelemetryRecord(0, packet.Mac, packet.Category, packet.Value, packet.Timestamp));
            await db.SaveChangesAsync();

            // LastSeen is stamped when the packet arrives (see TelemetryController), not here, so a long
            // backlog cannot make a device that is still sending look silent.
            _registry.Update(packet.Mac, d => d with { LastValue = packet.Value });

            // A command is the server talking to the device, not the device reporting, so it says nothing
            // about its health and must not clear a Disconnected alert.
            if (packet.Rank != PacketRank.Command)
            {
                if (packet.Rank == PacketRank.Critical)
                {
                    _alerts.TryRaise(device.Mac, AlertType.Critical, $"{device.Description} reading {packet.Value:0.##} is outside the safe range");
                }
                else
                {
                    _alerts.Clear(device.Mac, AlertType.Critical, $"{device.Description} is back in range ({packet.Value:0.##})");
                }
                _alerts.Clear(device.Mac, AlertType.Disconnected, $"{device.Description} reconnected");
            }

            _pipeline.RecordProcessed(packet, lane);
        }
    }
}
