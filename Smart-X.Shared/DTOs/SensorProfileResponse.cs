namespace Smart_X.Shared.DTOs
{
    public record SensorProfileResponse(int Id, string MacAddress, string Location, SensorCategory Category, DateTimeOffset RegisteredAt);
}
