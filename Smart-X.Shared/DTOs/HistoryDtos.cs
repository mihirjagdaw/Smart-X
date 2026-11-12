namespace Smart_X.Shared.DTOs
{
    public record HistoryReading(DateTimeOffset Timestamp, float Value);

    // How the query was answered: the size of the device's tree, how tall it is, and how long the lookup took.
    public record HistoryStats(int TreeSize, int TreeHeight, double ElapsedMicroseconds);

    // Total is every reading in the window. Items is just the requested page of them.
    public record HistoryPageResponse(
        string Mac,
        SensorCategory Category,
        DateTimeOffset? From,
        DateTimeOffset? To,
        int Total,
        int Skip,
        int Take,
        bool NewestFirst,
        List<HistoryReading> Items,
        HistoryStats Stats);

    public record HistorySeedResponse(string Mac, int Added, DateTimeOffset From, DateTimeOffset To, double ElapsedMilliseconds);
}
