namespace Smart_X.Shared.DTOs
{
    public record ActiveAlert(string Mac, string Type);

    public record AlertLogEntry(DateTimeOffset At, string Mac, string Type, string Message, bool Raised);

    public record AlertsResponse(List<ActiveAlert> Active, long Suppressed, List<AlertLogEntry> Recent);
}
