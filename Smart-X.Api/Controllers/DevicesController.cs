using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Processing;
using Smart_X.Shared;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DevicesController : ControllerBase
    {
        private static readonly char[] WordSeparators = { ' ', '/', '-', '_', '(', ')', ',', '.' };

        private readonly DeviceRegistry _registry;
        private readonly SimulatorControl _simulatorControl;
        private readonly AlertTracker _alerts;
        private readonly ActionHistory _actions;

        public DevicesController(DeviceRegistry registry, SimulatorControl simulatorControl, AlertTracker alerts, ActionHistory actions)
        {
            _registry = registry;
            _simulatorControl = simulatorControl;
            _alerts = alerts;
            _actions = actions;
        }

        // Finds active devices by words in their MAC, description or location, narrowed by category and
        // alert state. Devices with alerts come first. A text search is remembered so the insight engine can
        // learn what the operator looks at after what.
        [HttpGet("search")]
        public IActionResult Search(string? q, SensorCategory? category, AlertFilter? alert, bool record = true)
        {
            var terms = (q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant())
                .ToArray();

            var results = DeviceSummaries.Build(_registry.Snapshot(), _alerts.Snapshot().Active)
                .Where(s => (category is null || s.Category == category) && MatchesAlert(s.Alerts, alert) && MatchesTerms(s, terms))
                .ToList();

            // record=false lets a dashboard refresh the same search in the background without it counting as
            // something the operator did.
            if (record && terms.Length > 0)
            {
                _actions.Record(ActionKey.Search(results.Count == 1 ? results[0].Mac : string.Join(' ', terms)));
            }

            return Ok(results);
        }

        // Every term must start a word in the description or location ("pump a" finds Pump A, not every
        // device with an "a" somewhere). A term with a colon is matched against the MAC address instead.
        private static bool MatchesTerms(DeviceSummary device, string[] terms)
        {
            var words = $"{device.Description} {device.Location}".Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);

            return terms.All(t =>
                words.Any(w => w.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                || (t.Contains(':') && device.Mac.Contains(t, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool MatchesAlert(List<string> alerts, AlertFilter? filter) => filter switch
        {
            AlertFilter.Any => alerts.Count > 0,
            AlertFilter.None => alerts.Count == 0,
            AlertFilter.Critical => alerts.Contains(nameof(AlertType.Critical)),
            AlertFilter.Disconnected => alerts.Contains(nameof(AlertType.Disconnected)),
            _ => true
        };

        // Testing hook: makes the simulator stop (or resume) reporting for one device.
        [HttpPost("{mac}/simulate-offline")]
        public IActionResult ToggleSimulatedOffline(string mac)
        {
            if (!_registry.TryGet(mac, out var device))
            {
                return NotFound($"Device {mac} is not registered.");
            }

            return Ok(new SimulatedOfflineResponse(device.Mac, _simulatorControl.ToggleOffline(device.Mac)));
        }
    }
}
