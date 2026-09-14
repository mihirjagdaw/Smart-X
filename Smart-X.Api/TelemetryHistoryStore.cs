using Smart_X.Shared;

namespace Smart_X.Api
{
    public class TelemetryHistoryStore
    {
        private const int HistoryCapacityPerDevice = 100;

        private readonly Dictionary<string, SensorHistoryBuffer> _history = new();

        // converts a sensor's raw batch into SensorReading objects
        public List<SensorReading> ConvertBatch(string deviceId, SensorCategory category, float[] rawBatch)
        {
            var readings = new List<SensorReading>();
            foreach (var rawReading in rawBatch)
            {
                SensorReading temp = new SensorReading(deviceId, category, rawReading);
                readings.Add(temp);
            }
            return readings;
        }

        public void AddBatch(string deviceId, SensorCategory sensorCategory, float[] rawBatch)
        {
            var readings = ConvertBatch(deviceId, sensorCategory, rawBatch);

            if (!_history.TryGetValue(deviceId, out var buffer))
            {
                buffer = new SensorHistoryBuffer(HistoryCapacityPerDevice);
                _history[deviceId] = buffer;
            }

            foreach (var reading in readings)
            {
                buffer.Add(reading);
            }
        }

        public List<SensorReading> GetHistory(string deviceId)
        {
            return _history.TryGetValue(deviceId, out var buffer) ? buffer.ToList() : new List<SensorReading>();
        }

        public List<SensorReading> GetAllLatestReadings()
        {
            return _history.Values
                .Select(buffer => buffer.Latest)
                .Where(latest => latest is not null)
                .Select(latest => latest!)
                .ToList();
        }

        public void RemoveDevice(string deviceId)
        {
            _history.Remove(deviceId);
        }

        // Uses SensorReading's -, <, > operator overloads to compare the two most recent readings.
        public (SensorReading Delta, string Trend)? GetTrend(string deviceId)
        {
            if (!_history.TryGetValue(deviceId, out var buffer)) return null;

            var readings = buffer.ToList();
            if (readings.Count < 2) return null;

            var latest = readings[^1];
            var previous = readings[^2];
            var delta = latest - previous;
            var trend = latest > previous ? "Rising" : latest < previous ? "Falling" : "Stable";
            return (delta, trend);
        }

        // Uses SensorReading's + operator overload to combine the latest reading from every sensor in a category.
        public SensorReading? GetCategoryTotal(SensorCategory category)
        {
            SensorReading? total = null;
            foreach (var buffer in _history.Values)
            {
                var latest = buffer.Latest;
                if (latest is null || latest.Category != category) continue;
                total = total is null ? latest : total + latest;
            }
            return total;
        }
    }
}
