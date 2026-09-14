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
    }
}
