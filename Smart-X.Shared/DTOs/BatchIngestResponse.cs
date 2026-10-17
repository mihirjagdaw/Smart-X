namespace Smart_X.Shared.DTOs
{
    public record BatchIngestResponse(int Accepted, List<string> Rejected);
}
