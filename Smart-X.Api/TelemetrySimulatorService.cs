using Microsoft.EntityFrameworkCore;
using Smart_X.Api.Data;
using Smart_X.Shared;

namespace Smart_X.Api
{
    /// <summary>
    /// A background service that simulates telemetry data for active sensors in the system. 
    /// It periodically generates new telemetry readings based on the sensor category and stores them in both the database and an in-memory history store.
    /// </summary>
    public class TelemetrySimulatorService : BackgroundService
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TelemetryHistoryStore _historyStore;
        private readonly Random _random = new();
        private readonly Dictionary<string, float> _lastValues = new();

        public TelemetrySimulatorService(IServiceScopeFactory scopeFactory, TelemetryHistoryStore historyStore)
        {
            _scopeFactory = scopeFactory;
            _historyStore = historyStore;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Tick();
                }
                catch
                {

                }

                await Task.Delay(TickInterval, stoppingToken);
            }
        }

        private async Task Tick()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var activeSensors = await dbContext.Sensors.Where(s => s.IsActive).ToListAsync();

            foreach (var sensor in activeSensors)
            {
                var previous = _lastValues.TryGetValue(sensor.MacAddress, out var lastValue)
                    ? lastValue
                    : GetStartingValue(sensor.Category);

                var next = Drift(sensor.Category, previous);
                _lastValues[sensor.MacAddress] = next;

                _historyStore.AddBatch(sensor.MacAddress, sensor.Category, new[] { next });
                dbContext.TelemetryRecords.Add(new TelemetryRecord(0, sensor.MacAddress, sensor.Category, next, DateTimeOffset.UtcNow));
            }

            await dbContext.SaveChangesAsync();
        }

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

            var delta = (float)(_random.NextDouble() - 0.5);
            var next = previous + delta;

            return category switch
            {
                SensorCategory.Environmental => Math.Clamp(next, 0f, 100f),
                SensorCategory.PowerConsumption => Math.Clamp(next, 0f, 3000f),
                _ => next
            };
        }
    }
}
