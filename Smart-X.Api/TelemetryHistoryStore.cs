using Smart_X.Shared;

namespace Smart_X.Api
{
    public class TelemetryHistoryStore
    {
        private readonly Dictionary<string, List<SensorReading>> _history = new();

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

            if (_history.TryGetValue(deviceId, out var existing))
            {
                existing.AddRange(readings);
            }
            else
            {
                _history[deviceId] = readings;
            }

        }

        public List<SensorReading> GetHistory(string deviceId)
        {
            return _history.TryGetValue(deviceId, out var history) ? history : new List<SensorReading>();
        }

        public List<SensorReading> GetAllLatestReadings()
        {
            return _history.Values
                .Where(readings => readings.Count > 0)
                .Select(readings => readings[^1])
                .ToList();
        }
    }
}
