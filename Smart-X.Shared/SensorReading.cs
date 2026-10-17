namespace Smart_X.Shared
{
    public class SensorReading
    {
        public string DeviceId { get; }

        public SensorCategory Category { get; }

        public float Value { get; }

        public DateTimeOffset Timestamp { get; }

        public SensorReading(string deviceId, SensorCategory category, float value)
            : this(deviceId, category, value, DateTimeOffset.UtcNow)
        {
        }

        public SensorReading(string deviceId, SensorCategory category, float value, DateTimeOffset timestamp)
        {
            DeviceId = deviceId;
            Category = category;
            Value = value;
            Timestamp = timestamp;
        }

        public static SensorReading operator +(SensorReading a, SensorReading b)
        {
            if (a.Category != b.Category)
            {
                throw new InvalidOperationException("Cannot add SensorReadings with different Categories.");
            }
            // decide how to handle DeviceId - here we just take the first one
            return new SensorReading(a.DeviceId, a.Category, a.Value + b.Value);
        }


        // useful for calculating differences in readings, e.g., for delta calculations (how much did the soil moisture change (same sensor, diff time))
        public static SensorReading operator -(SensorReading a, SensorReading b)
        {
            if (a.DeviceId != b.DeviceId)
            {
                throw new InvalidOperationException("Cannot subtract SensorReadings with different DeviceIds.");
            }
            // decide how to handle DeviceId - here we just take the first one
            return new SensorReading(a.DeviceId, a.Category, a.Value - b.Value);
        }

        public static bool operator <(SensorReading a, SensorReading b)
        {
            if (a.Category != b.Category)
            {
                throw new InvalidOperationException("Cannot compare SensorReadings with different Categories.");
            }
            bool isLessThan = a.Value < b.Value;
            return isLessThan;
        }

        public static bool operator >(SensorReading a, SensorReading b)
        {
            if (a.Category != b.Category)
            {
                throw new InvalidOperationException("Cannot compare SensorReadings with different Categories.");
            }
            bool isGreaterThan = a.Value > b.Value;
            return isGreaterThan;
        }
    }

}
