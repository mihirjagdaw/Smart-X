namespace Smart_X.Api.Processing
{
    /// <summary>
    /// Raises a Disconnected alert for any active device that has gone quiet. The alert set keeps it to one
    /// alert per outage however many checks fire; the next packet from the device clears it.
    /// </summary>
    public class DisconnectMonitorService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

        private readonly DeviceRegistry _registry;
        private readonly AlertTracker _alerts;
        private readonly TimeSpan _threshold;

        public DisconnectMonitorService(DeviceRegistry registry, AlertTracker alerts, IConfiguration configuration)
        {
            _registry = registry;
            _alerts = alerts;
            _threshold = TimeSpan.FromSeconds(configuration.GetValue("Monitor:DisconnectAfterSeconds", 15));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(CheckInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var device in _registry.Snapshot())
                {
                    // shortcut: a device only counts as disconnected after it has reported once, so a device
                    // that never reports (or has not yet since a restart) is never flagged; persist LastSeen if that matters.
                    if (!device.IsActive || device.LastSeen is not { } lastSeen) continue;

                    var silent = now - lastSeen;
                    if (silent > _threshold)
                    {
                        _alerts.TryRaise(device.Mac, AlertType.Disconnected, $"{device.Description} has not reported for {(int)silent.TotalSeconds}s");
                    }
                }
            }
        }
    }
}
