namespace Smart_X.Shared.DTOs
{
    // Kind is RunCommand (Command is "on" or "off"), ReviewDevice, or SearchQuery.
    public record Suggestion(string Kind, string Text, string Reason, double Confidence, string? DeviceId, string? Command, string? Query);

    public record DeviceWarning(string Mac, string Description, string Severity, string Message);

    public record InsightsResponse(List<Suggestion> SuggestedActions, List<DeviceWarning> Warnings);
}
