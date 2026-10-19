namespace Smart_X.Shared.DTOs
{
    // Everything the Command Stream page shows, in one response so the dashboard polls with a single request.
    public record StreamSnapshot(
        PipelineStats Stats,
        List<ProcessedEvent> Recent,
        AlertsResponse Alerts,
        List<CommandResponse> Commands,
        InsightsResponse Insights,
        List<DeviceSummary> Devices);
}
