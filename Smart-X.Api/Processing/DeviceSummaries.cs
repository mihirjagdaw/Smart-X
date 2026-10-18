using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Processing
{
    public static class DeviceSummaries
    {
        // Active devices with their current alerts attached, devices in trouble first.
        public static List<DeviceSummary> Build(IEnumerable<DeviceState> devices, List<ActiveAlert> activeAlerts)
        {
            var alertsByMac = activeAlerts
                .GroupBy(a => a.Mac, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(a => a.Type).ToList(), StringComparer.OrdinalIgnoreCase);

            return devices
                .Where(d => d.IsActive)
                .Select(d => new DeviceSummary(
                    d.Mac, d.Category, d.Location, d.Description, d.LastValue, d.LastSeen, d.OverrideValue is not null,
                    alertsByMac.GetValueOrDefault(d.Mac) ?? new List<string>()))
                .OrderByDescending(s => s.Alerts.Count)
                .ThenBy(s => s.Description)
                .ToList();
        }
    }
}
