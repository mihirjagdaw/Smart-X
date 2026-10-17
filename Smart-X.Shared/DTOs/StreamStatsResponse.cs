namespace Smart_X.Shared.DTOs
{
    public record PipelineStats(int StandardDepth, int PriorityDepth, long ProcessedStandard, long ProcessedPriority, long Dropped);

    public record ProcessedEvent(long Order, string Mac, SensorCategory Category, float Value, string Lane, DateTimeOffset ArrivedAt, DateTimeOffset ProcessedAt);

    public record StreamStatsResponse(PipelineStats Stats, List<ProcessedEvent> Recent);
}
