namespace Smart_X.Shared.DTOs
{
    public record TelemetryIngestRequest(string DeviceId, SensorCategory Category, string ValueType, float? FloatValue, int? IntValue, bool? BoolValue);
}
