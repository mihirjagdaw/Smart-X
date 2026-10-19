using Smart_X.Client.Models;
using Smart_X.Shared;

namespace Smart_X.Client.Services
{
    /// <summary>
    /// Everything the operator has set up on screen. It lives for the life of the app instead of the life of
    /// a page, so moving between modules does not lose half-typed forms, filters or selections.
    /// </summary>
    public class AppState
    {
        // Sensor Data Ingestion page.
        public SensorFormModel SensorForm { get; set; } = new();
        public TelemetryFormModel TelemetryForm { get; } = new();
        public string FileDeviceId { get; set; } = "";

        // Real-Time Command Stream page.
        public string Query { get; set; } = "";
        public SensorCategory? Category { get; set; }
        public AlertFilter? Alert { get; set; }
        public string? SelectedMac { get; set; }
        public int TimelineMinutes { get; set; } = 5;
    }
}
