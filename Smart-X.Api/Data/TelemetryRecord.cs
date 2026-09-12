using Smart_X.Shared;

namespace Smart_X.Api.Data
{
    public class TelemetryRecord
    {
        public int Id { get; set; }

        public string DeviceId { get; set; }

        public SensorCategory Category { get; set; }

        public float Value { get; set; }

        public DateTimeOffset Timestamp { get; set; }

        public TelemetryRecord(int id, string deviceId, SensorCategory category, float value, DateTimeOffset timestamp)
        {
            Id = id;
            DeviceId = deviceId;
            Category = category;
            Value = value;
            Timestamp = timestamp;
        }
    }
}
