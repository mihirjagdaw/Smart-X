namespace Smart_X.Shared.DTOs
{
    public record SensorProfileResponse(int Id, string MacAddress, string Location, SensorCategory Category, string Description, DateTimeOffset RegisteredAt, bool IsActive);
}
