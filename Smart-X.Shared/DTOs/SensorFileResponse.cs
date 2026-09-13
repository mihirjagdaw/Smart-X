namespace Smart_X.Shared.DTOs
{
    public record SensorFileResponse(int Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);
}
