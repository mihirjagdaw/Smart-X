using System.Net.Http.Json;
using Smart_X.Api.Processing;
using Smart_X.Shared;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api
{
    /// <summary>
    /// A background service that plays the part of the physical devices. Every tick it drifts each active
    /// sensor's value and posts the readings to the API's own batch endpoint over HTTP, so simulated data
    /// takes exactly the same path (validation, queueing, processing) as data from a real device.
    /// </summary>
    public class TelemetrySimulatorService : BackgroundService
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

        private readonly DeviceRegistry _registry;
        private readonly ILogger<TelemetrySimulatorService> _logger;
        private readonly HttpClient _http;
        private readonly Random _random = new();
        private readonly Dictionary<string, float> _lastValues = new();

        public TelemetrySimulatorService(DeviceRegistry registry, IConfiguration configuration, ILogger<TelemetrySimulatorService> logger)
        {
            _registry = registry;
            _logger = logger;
            _http = new HttpClient { BaseAddress = new Uri(configuration["Simulator:ApiBaseUrl"] ?? "http://localhost:5256") };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Delay first so the web server is listening before the first post.
                await Task.Delay(TickInterval, stoppingToken);

                try
                {
                    await Tick(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Simulator tick failed; retrying next tick");
                }
            }
        }

        private async Task Tick(CancellationToken cancellationToken)
        {
            var requests = new List<TelemetryIngestRequest>();

            foreach (var device in _registry.Snapshot().Where(d => d.IsActive))
            {
                var previous = _lastValues.TryGetValue(device.Mac, out var lastValue)
                    ? lastValue
                    : GetStartingValue(device.Category);

                var next = Drift(device.Category, previous);
                _lastValues[device.Mac] = next;
                requests.Add(ToRequest(device, next));
            }

            if (requests.Count == 0) return;

            var response = await _http.PostAsJsonAsync("api/Telemetry/batch", requests, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        // Devices report in the value type their category uses: float, int or on/off.
        private static TelemetryIngestRequest ToRequest(DeviceState device, float value) => device.Category switch
        {
            SensorCategory.PowerConsumption => new(device.Mac, device.Category, "int", null, (int)Math.Round(value), null),
            SensorCategory.Actuator => new(device.Mac, device.Category, "bool", null, null, value >= 1f),
            _ => new(device.Mac, device.Category, "float", value, null, null)
        };

        private static float GetStartingValue(SensorCategory category) => category switch
        {
            SensorCategory.Environmental => 45f,
            SensorCategory.PowerConsumption => 1200f,
            SensorCategory.Actuator => 0f,
            _ => 0f
        };

        private float Drift(SensorCategory category, float previous)
        {
            if (category == SensorCategory.Actuator)
            {
                return _random.NextDouble() < 0.1 ? (previous == 0f ? 1f : 0f) : previous;
            }

            // Power readings are whole watts, so they need a bigger step than a 0-100 reading to move visibly.
            var step = category == SensorCategory.PowerConsumption ? 20f : 1f;
            var next = previous + (float)(_random.NextDouble() - 0.5) * step;

            return category switch
            {
                SensorCategory.Environmental => Math.Clamp(next, 0f, 100f),
                SensorCategory.PowerConsumption => Math.Clamp(next, 0f, 3000f),
                _ => next
            };
        }

        public override void Dispose()
        {
            _http.Dispose();
            base.Dispose();
        }
    }
}
