using Smart_X.Api.Data;
using Smart_X.Shared;

namespace Smart_X.Api.Processing
{
    public record DeviceState(
        string Mac,
        SensorCategory Category,
        string Location,
        string Description,
        bool IsActive,
        DateTimeOffset? LastSeen = null,
        float? LastValue = null,
        float? OverrideValue = null);

    /// <summary>
    /// Live registered devices keyed by MAC address, so each incoming packet is an O(1) lookup
    /// instead of a database query. Entries are immutable records swapped under a lock,
    /// so a reader on another thread always sees a consistent snapshot.
    /// </summary>
    public class DeviceRegistry
    {
        private readonly Dictionary<string, DeviceState> _devices = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public void Load(IEnumerable<SensorProfile> sensors)
        {
            lock (_lock)
            {
                _devices.Clear();
                foreach (var sensor in sensors)
                {
                    _devices[sensor.MacAddress] = ToState(sensor);
                }
            }
        }

        public void Upsert(SensorProfile sensor)
        {
            lock (_lock)
            {
                var fresh = ToState(sensor);
                _devices[sensor.MacAddress] = _devices.TryGetValue(sensor.MacAddress, out var existing)
                    ? fresh with { LastSeen = existing.LastSeen, LastValue = existing.LastValue, OverrideValue = existing.OverrideValue }
                    : fresh;
            }
        }

        public bool Remove(string mac)
        {
            lock (_lock)
            {
                return _devices.Remove(mac);
            }
        }

        public bool Update(string mac, Func<DeviceState, DeviceState> change)
        {
            lock (_lock)
            {
                if (!_devices.TryGetValue(mac, out var current)) return false;
                _devices[mac] = change(current);
                return true;
            }
        }

        public bool TryGet(string mac, out DeviceState state)
        {
            lock (_lock)
            {
                return _devices.TryGetValue(mac, out state!);
            }
        }

        public List<DeviceState> Snapshot()
        {
            lock (_lock)
            {
                return _devices.Values.ToList();
            }
        }

        private static DeviceState ToState(SensorProfile sensor) =>
            new(sensor.MacAddress, sensor.Category, sensor.Location, sensor.Description, sensor.IsActive);
    }
}
