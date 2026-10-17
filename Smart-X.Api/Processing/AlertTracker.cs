using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Processing
{
    public enum AlertType { Critical, Disconnected }

    public readonly record struct AlertKey(string Mac, AlertType Type);

    /// <summary>
    /// The set of currently active alerts. A HashSet only stores each (device, alert type) once, so a
    /// condition that persists, such as a node that stays offline, raises one alert instead of one per check.
    /// </summary>
    public class AlertTracker
    {
        private const int LogCapacity = 100;

        private readonly HashSet<AlertKey> _active = new();
        private readonly Queue<AlertLogEntry> _log = new();
        private readonly object _lock = new();
        private long _suppressed;

        // True only when the alert is new. A repeat is counted and dropped.
        public bool TryRaise(string mac, AlertType type, string message)
        {
            lock (_lock)
            {
                if (!_active.Add(new AlertKey(mac, type)))
                {
                    _suppressed++;
                    return false;
                }

                Log(mac, type, message, raised: true);
                return true;
            }
        }

        // True only when there was an active alert to clear.
        public bool Clear(string mac, AlertType type, string message)
        {
            lock (_lock)
            {
                if (!_active.Remove(new AlertKey(mac, type))) return false;

                Log(mac, type, message, raised: false);
                return true;
            }
        }

        public void ClearDevice(string mac)
        {
            lock (_lock)
            {
                foreach (var key in _active.Where(k => string.Equals(k.Mac, mac, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    _active.Remove(key);
                    Log(key.Mac, key.Type, "Device deactivated or removed", raised: false);
                }
            }
        }

        public AlertsResponse Snapshot()
        {
            lock (_lock)
            {
                var active = _active
                    .OrderBy(k => k.Mac, StringComparer.OrdinalIgnoreCase).ThenBy(k => k.Type)
                    .Select(k => new ActiveAlert(k.Mac, k.Type.ToString()))
                    .ToList();

                // Newest first, which is how the dashboard wants to show it.
                return new AlertsResponse(active, _suppressed, _log.Reverse().ToList());
            }
        }

        private void Log(string mac, AlertType type, string message, bool raised)
        {
            _log.Enqueue(new AlertLogEntry(DateTimeOffset.UtcNow, mac, type.ToString(), message, raised));
            while (_log.Count > LogCapacity) _log.Dequeue();
        }
    }
}
