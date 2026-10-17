namespace Smart_X.Shared.DTOs
{
    public record CommandRequest(string DeviceId, bool On);

    public record CommandResponse(int Id, string Description, DateTimeOffset At, List<string> Devices);
}
