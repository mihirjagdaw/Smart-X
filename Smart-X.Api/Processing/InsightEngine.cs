using Smart_X.Shared;
using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Processing
{
    /// <summary>
    /// Turns what the system knows into Suggested Actions and Automated Insights: follow-ups the operator
    /// usually makes after what is happening now (learned by ActionHistory), plus devices that look like
    /// they are heading for trouble (read from each device's timeline).
    /// </summary>
    public class InsightEngine
    {
        private const int MaxSuggestions = 5;
        private const int TrendReadings = 12;
        private const int MinTrendReadings = 8;
        private const double MinFitQuality = 0.6;
        private const double JumpZScore = 4;
        private static readonly TimeSpan BreachHorizon = TimeSpan.FromMinutes(10);

        private readonly DeviceRegistry _registry;
        private readonly AlertTracker _alerts;
        private readonly ActionHistory _actions;
        private readonly TelemetryHistoryStore _store;

        public InsightEngine(DeviceRegistry registry, AlertTracker alerts, ActionHistory actions, TelemetryHistoryStore store)
        {
            _registry = registry;
            _alerts = alerts;
            _actions = actions;
            _store = store;
        }

        public InsightsResponse Build()
        {
            var devices = _registry.Snapshot().Where(d => d.IsActive).ToDictionary(d => d.Mac, StringComparer.OrdinalIgnoreCase);
            var activeAlerts = _alerts.Snapshot().Active;

            return new InsightsResponse(SuggestActions(devices, activeAlerts), FindWarnings(devices, activeAlerts));
        }

        private List<Suggestion> SuggestActions(Dictionary<string, DeviceState> devices, List<ActiveAlert> activeAlerts)
        {
            // What is going on now: every active alert, plus the operator's last few actions.
            var triggers = activeAlerts.Select(a => ActionKey.Alert(a.Mac, a.Type)).Concat(_actions.RecentKeys(3));

            // Best first: highest confidence, then the most evidence. This is a top-k selection, so a heap fits.
            var ranked = new PriorityQueue<Suggestion, (double NegConfidence, int NegCount)>();
            foreach (var candidate in _actions.Candidates(triggers))
            {
                var suggestion = ToSuggestion(candidate, devices);
                if (suggestion is not null) ranked.Enqueue(suggestion, (-candidate.Confidence, -candidate.Count));
            }

            var result = new List<Suggestion>();
            var seen = new HashSet<string>();
            while (result.Count < MaxSuggestions && ranked.TryDequeue(out var next, out _))
            {
                if (seen.Add($"{next.Kind}|{next.DeviceId}|{next.Command}|{next.Query}")) result.Add(next);
            }
            return result;
        }

        private static Suggestion? ToSuggestion(SuggestionCandidate candidate, Dictionary<string, DeviceState> devices)
        {
            var (kind, target, detail) = ActionKey.Parse(candidate.FollowerKey);
            var reason = $"After {Describe(candidate.TriggerKey, devices)} you did this {candidate.Count} of {candidate.TriggerOccurrences} times ({candidate.Confidence:P0})";

            switch (kind)
            {
                case "cmd":
                    if (!devices.TryGetValue(target, out var actuator) || actuator.Category != SensorCategory.Actuator) return null;

                    var on = detail == "on";
                    // Nothing to suggest if the device is already where the operator would put it.
                    if ((actuator.OverrideValue ?? actuator.LastValue) is { } state && (state >= 1f) == on) return null;

                    return new Suggestion("RunCommand", $"Turn {(on ? "ON" : "OFF")} {actuator.Description}", reason,
                        candidate.Confidence, actuator.Mac, on ? "on" : "off", null);

                case "search":
                    return devices.TryGetValue(target, out var device)
                        ? new Suggestion("ReviewDevice", $"Check {device.Description}", reason, candidate.Confidence, device.Mac, null, null)
                        : new Suggestion("SearchQuery", $"Search for \"{target}\"", reason, candidate.Confidence, null, null, target);

                default:
                    return null;
            }
        }

        private static string Describe(string key, Dictionary<string, DeviceState> devices)
        {
            var (kind, target, detail) = ActionKey.Parse(key);
            var name = devices.TryGetValue(target, out var device) ? device.Description : target;

            return kind switch
            {
                "alert" => detail == nameof(AlertType.Critical) ? $"{name} went critical" : $"{name} went offline",
                "cmd" => $"you turned {name} {detail}",
                _ => $"you searched for {name}"
            };
        }

        private List<DeviceWarning> FindWarnings(Dictionary<string, DeviceState> devices, List<ActiveAlert> activeAlerts)
        {
            var warnings = new List<DeviceWarning>();
            var critical = activeAlerts.Where(a => a.Type == nameof(AlertType.Critical)).Select(a => a.Mac).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var alert in activeAlerts)
            {
                if (!devices.TryGetValue(alert.Mac, out var device)) continue;

                warnings.Add(alert.Type == nameof(AlertType.Critical)
                    ? new DeviceWarning(device.Mac, device.Description, "Critical", $"Reading {device.LastValue:0.##} is outside the safe range")
                    : new DeviceWarning(device.Mac, device.Description, "Warning", "Not reporting"));
            }

            // On/off devices have no trend to read.
            foreach (var device in devices.Values.Where(d => d.Category != SensorCategory.Actuator))
            {
                var readings = _store.GetTimeline(device.Mac, 10).TakeLast(TrendReadings).ToList();
                if (readings.Count < MinTrendReadings) continue;

                if (JumpMessage(readings) is { } jump)
                {
                    warnings.Add(new DeviceWarning(device.Mac, device.Description, "Warning", jump));
                }

                if (!critical.Contains(device.Mac) && BreachMessage(device.Category, readings) is { } breach)
                {
                    warnings.Add(new DeviceWarning(device.Mac, device.Description, "Warning", breach));
                }
            }

            return warnings.OrderBy(w => w.Severity == "Critical" ? 0 : 1).ThenBy(w => w.Description).ToList();
        }

        // A reading that moves far more than this device normally moves between readings.
        private static string? JumpMessage(List<SensorReading> readings)
        {
            var steps = readings.Zip(readings.Skip(1), (a, b) => (double)(b.Value - a.Value)).ToList();
            var latest = steps[^1];
            var earlier = steps.Take(steps.Count - 1).ToList();

            var mean = earlier.Average();
            var spread = Math.Max(Math.Sqrt(earlier.Average(s => (s - mean) * (s - mean))), 0.01);

            return Math.Abs(latest - mean) / spread >= JumpZScore
                ? $"Unusual jump of {latest:+0.##;-0.##} since the last reading"
                : null;
        }

        // Fits a straight line to the recent readings and, if it is a clean trend heading for the edge of the
        // safe range, says how soon it gets there.
        private static string? BreachMessage(SensorCategory category, List<SensorReading> readings)
        {
            var start = readings[0].Timestamp;
            var xs = readings.Select(r => (r.Timestamp - start).TotalSeconds).ToArray();
            var ys = readings.Select(r => (double)r.Value).ToArray();

            var meanX = xs.Average();
            var meanY = ys.Average();
            var sxx = xs.Sum(x => (x - meanX) * (x - meanX));
            var syy = ys.Sum(y => (y - meanY) * (y - meanY));
            var sxy = xs.Zip(ys, (x, y) => (x - meanX) * (y - meanY)).Sum();
            if (sxx <= 0 || syy <= 0) return null;

            var slope = sxy / sxx;
            if (sxy * sxy / (sxx * syy) < MinFitQuality) return null;

            double? limit = category switch
            {
                SensorCategory.Environmental when slope < 0 => AlertRules.EnvironmentalMin,
                SensorCategory.Environmental => AlertRules.EnvironmentalMax,
                SensorCategory.PowerConsumption when slope > 0 => AlertRules.PowerMax,
                _ => null
            };

            var last = ys[^1];
            if (limit is null || AlertRules.Evaluate(category, (float)last) == Severity.Critical) return null;

            var seconds = (limit.Value - last) / slope;
            if (seconds <= 0 || seconds > BreachHorizon.TotalSeconds) return null;

            var eta = seconds < 60 ? "under a minute" : $"about {Math.Round(seconds / 60):0} min";
            return $"Trending {(slope < 0 ? "down" : "up")} {Math.Abs(slope * 60):0.#} per minute; predicted to leave the safe range in {eta}";
        }
    }
}
