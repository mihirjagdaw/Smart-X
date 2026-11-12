using Smart_X.Api.Processing;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Network
{
    /// <summary>
    /// Keeps the mesh picture up to date in the background. Once a second it checks which devices exist and
    /// which have gone silent; only when that has changed does it rebuild the analysis and record what changed
    /// (devices down, routes that moved) in the heal log. Requests just read the latest analysis.
    /// </summary>
    public class TopologyService : BackgroundService
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
        private const int HealCapacity = 100;

        private readonly DeviceRegistry _registry;
        private readonly AlertTracker _alerts;
        private readonly MeshOptions _options;
        private readonly ILogger<TopologyService> _logger;
        private readonly Queue<HealEvent> _healLog = new();
        private readonly object _lock = new();

        private MeshTopology? _topology;
        private TopologyAnalysis? _analysis;
        private string _analysisKey = "";
        private long _version;

        public TopologyService(DeviceRegistry registry, AlertTracker alerts, MeshOptions options, ILogger<TopologyService> logger)
        {
            _registry = registry;
            _alerts = alerts;
            _options = options;
            _logger = logger;
        }

        /// <summary>The latest analysis. The first call builds it if the background loop has not yet.</summary>
        public TopologyAnalysis Current
        {
            get
            {
                lock (_lock)
                {
                    if (_analysis is null) Refresh();
                    return _analysis!;
                }
            }
        }

        /// <summary>Goes up by one every time the analysis changes.</summary>
        public long Version
        {
            get
            {
                lock (_lock)
                {
                    return _version;
                }
            }
        }

        /// <summary>The most recent changes to the mesh, newest first.</summary>
        public List<HealEvent> HealLog()
        {
            lock (_lock)
            {
                return _healLog.Reverse().ToList();
            }
        }

        /// <summary>
        /// Looks at the devices and alerts as they are now. Returns true when that changed the analysis.
        /// The expensive part, laying out and linking the devices, only happens when a device is added, removed
        /// or moved; a device going down or coming back only reruns the analysis.
        /// </summary>
        public bool Refresh()
        {
            lock (_lock)
            {
                var devices = _registry.Snapshot().Where(d => d.IsActive).ToList();
                var ids = devices.Select(d => d.Mac).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var active = _alerts.Snapshot().Active.Where(a => ids.Contains(a.Mac)).ToList();
                var down = active.Where(a => a.Type == nameof(AlertType.Disconnected)).Select(a => a.Mac).ToList();
                var critical = active.Where(a => a.Type == nameof(AlertType.Critical)).Select(a => a.Mac).ToList();

                var fingerprint = MeshBuilder.Fingerprint(devices);
                if (_topology is null || _topology.Fingerprint != fingerprint)
                {
                    _topology = MeshBuilder.Build(devices, _options);
                }

                var key = $"{fingerprint}#{string.Join(',', down.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))}#{string.Join(',', critical.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))}";
                if (_analysis is not null && key == _analysisKey) return false;

                var next = TopologyAnalyzer.Analyze(_topology, down, critical);
                // The log is read newest first, so queue each tick's events backwards to keep the cause above its effects.
                foreach (var change in TopologyAnalyzer.Diff(_analysis, next, DateTimeOffset.UtcNow).AsEnumerable().Reverse())
                {
                    _healLog.Enqueue(change);
                    while (_healLog.Count > HealCapacity) _healLog.Dequeue();
                }

                if (!next.MstVerified)
                {
                    _logger.LogWarning("Prim and Kruskal disagree on the backbone cost ({Prim} vs {Kruskal}); one of the algorithms has a bug",
                        next.Mst.TotalWeight, next.KruskalTotal);
                }

                _analysis = next;
                _analysisKey = key;
                _version++;
                return true;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(RefreshInterval);
            do
            {
                try
                {
                    await Task.Run(Refresh, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Topology refresh failed; keeping the previous topology");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
