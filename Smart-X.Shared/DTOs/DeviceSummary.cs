namespace Smart_X.Shared.DTOs
{
    public record DeviceSummary(
        string Mac,
        SensorCategory Category,
        string Location,
        string Description,
        float? LastValue,
        DateTimeOffset? LastSeen,
        bool Overridden,
        List<string> Alerts);
}
