namespace Smart_X.Api.Processing
{
    /// <summary>
    /// Devices the simulator should pretend are unplugged, so a disconnect can be demonstrated on demand.
    /// </summary>
    public class SimulatorControl
    {
        private readonly HashSet<string> _offline = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        // Returns the new state: true when the device is now offline.
        public bool ToggleOffline(string mac)
        {
            lock (_lock)
            {
                if (_offline.Remove(mac)) return false;

                _offline.Add(mac);
                return true;
            }
        }

        public bool IsOffline(string mac)
        {
            lock (_lock)
            {
                return _offline.Contains(mac);
            }
        }
    }
}
