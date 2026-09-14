namespace Smart_X.Shared.DTOs
{
    public record CreateSensorRequest(string MacAddress, string Location, SensorCategory Category, string Description);
}
