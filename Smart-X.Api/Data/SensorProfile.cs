using Smart_X.Shared;

namespace Smart_X.Api.Data
{
    public class SensorProfile
    {
        public int Id { get; set; }

        public string MacAddress { get; set; }

        public string Location { get; set; }

        public SensorCategory Category { get; set; }

        public string Description { get; set; }

        public DateTimeOffset RegisteredAt { get; set; }

        public bool IsActive { get; set; }

        public SensorProfile(int id, string macAddress, string location, SensorCategory category, string description, DateTimeOffset registeredAt, bool isActive)
        {
            Id = id;
            MacAddress = macAddress;
            Location = location;
            Category = category;
            Description = description;
            RegisteredAt = registeredAt;
            IsActive = isActive;
        }
    }
}
