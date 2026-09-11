namespace Smart_X.Shared
{
    /// <summary>
    /// Generic wrapper for a single telemetry reading. T is constrained to
    /// struct so float/int/bool payloads are stored and read as their real
    /// value type - no boxing to object, unlike a non-generic "object Value"
    /// design would require.
    /// </summary>
    public class TelemetryPacket<T> where T : struct
    {
        public string DeviceId { get; }
        public SensorCategory Category { get; }
        public T Value { get; }
        public DateTimeOffset Timestamp { get; }

        public TelemetryPacket(string deviceId, SensorCategory category, T value)
        {
            DeviceId = deviceId;
            Category = category;
            Value = value;
            Timestamp = DateTimeOffset.UtcNow;
        }

        public override string ToString() =>
            $"[{Timestamp:HH:mm:ss}] {DeviceId} ({Category}) = {Value}";
    }
}
