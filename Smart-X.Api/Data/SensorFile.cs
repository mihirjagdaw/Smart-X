namespace Smart_X.Api.Data
{
    public class SensorFile
    {
        public int Id { get; set; }

        public int SensorId { get; set; }

        public string FileName { get; set; }

        public string StoredFileName { get; set; }

        public string ContentType { get; set; }

        public long SizeBytes { get; set; }

        public DateTimeOffset UploadedAt { get; set; }

        public SensorFile(int id, int sensorId, string fileName, string contentType, long sizeBytes, DateTimeOffset uploadedAt)
        {
            Id = id;
            SensorId = sensorId;
            FileName = fileName;
            StoredFileName = Guid.NewGuid() + Path.GetExtension(fileName);
            ContentType = contentType;
            SizeBytes = sizeBytes;
            UploadedAt = uploadedAt;
        }

    }
}
