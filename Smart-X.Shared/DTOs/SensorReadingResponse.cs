namespace Smart_X.Shared.DTOs
{
    public record SensorReadingResponse(string DeviceId, SensorCategory Category, float Value, DateTimeOffset Timestamp);
}
