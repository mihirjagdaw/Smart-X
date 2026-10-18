namespace Smart_X.Api.Processing
{
    /// <summary>
    /// Keys for the things the operator and the system do. A MAC address contains colons, so the target is
    /// separated from the detail with a bar.
    /// </summary>
    public static class ActionKey
    {
        public static string Alert(string mac, string type) => $"alert:{mac}|{type}";

        public static string Command(string mac, bool on) => $"cmd:{mac}|{(on ? "on" : "off")}";

        // The target is a MAC when the search matched exactly one device, otherwise the normalised query text.
        public static string Search(string target) => $"search:{target}";

        public static (string Kind, string Target, string Detail) Parse(string key)
        {
            var colon = key.IndexOf(':');
            var kind = key[..colon];
            var rest = key[(colon + 1)..];
            if (kind == "search") return (kind, rest, "");

            var bar = rest.LastIndexOf('|');
            return (kind, rest[..bar], rest[(bar + 1)..]);
        }
    }

    public record SuggestionCandidate(string TriggerKey, string FollowerKey, int Count, int TriggerOccurrences)
    {
        public double Confidence => (double)Count / TriggerOccurrences;
    }

    /// <summary>
    /// Learns which actions tend to follow which. Every event is compared with the few that came just before
    /// it, and the pair is counted in a Dictionary of Dictionaries: transitions[trigger][follower] is how many
    /// times the follower came after the trigger. Lookups for a trigger that is active right now are O(1).
    /// </summary>
    public class ActionHistory
    {
        public const int MinSupport = 2;
        public const double MinConfidence = 0.5;

        private const int MaxPriors = 5;
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(120);

        private sealed record RecentEvent(string Key, DateTimeOffset At, HashSet<string> Followers);

        private readonly Func<DateTimeOffset> _clock;
        private readonly List<RecentEvent> _recent = new();
        private readonly Dictionary<string, Dictionary<string, int>> _transitions = new();
        private readonly Dictionary<string, int> _occurrences = new();
        private readonly Dictionary<string, DateTimeOffset> _lastAt = new();
        private readonly object _lock = new();

        // The clock is injectable so a test can replay hours of behaviour without waiting.
        public ActionHistory(Func<DateTimeOffset>? clock = null)
        {
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        // shortcut: lives in memory and grows with the number of distinct actions, so it resets on restart;
        // persist the two dictionaries if the learning needs to outlive the process.
        public void Record(string key)
        {
            lock (_lock)
            {
                var now = _clock();
                _recent.RemoveAll(e => now - e.At > Window);

                // Repeating the same action back to back is one intent, not several.
                if (_recent.Count > 0 && _recent[^1].Key == key) return;

                foreach (var prior in Enumerable.Reverse(_recent).Take(MaxPriors))
                {
                    // An earlier event can be followed by a given action only once, so a pair's count can
                    // never exceed the number of times its trigger happened.
                    if (prior.Key == key || !prior.Followers.Add(key)) continue;

                    if (!_transitions.TryGetValue(prior.Key, out var followers))
                    {
                        followers = new Dictionary<string, int>();
                        _transitions[prior.Key] = followers;
                    }
                    followers[key] = followers.GetValueOrDefault(key) + 1;
                }

                _occurrences[key] = _occurrences.GetValueOrDefault(key) + 1;
                _lastAt[key] = now;
                _recent.Add(new RecentEvent(key, now, new HashSet<string>()));
            }
        }

        // Newest first, limited to events still inside the window.
        public List<string> RecentKeys(int count)
        {
            lock (_lock)
            {
                var now = _clock();
                return Enumerable.Reverse(_recent).Where(e => now - e.At <= Window).Take(count).Select(e => e.Key).ToList();
            }
        }

        // Follow-ups worth suggesting for triggers that are happening now.
        public List<SuggestionCandidate> Candidates(IEnumerable<string> triggerKeys)
        {
            lock (_lock)
            {
                var result = new List<SuggestionCandidate>();

                foreach (var trigger in triggerKeys.Distinct())
                {
                    if (!_transitions.TryGetValue(trigger, out var followers)) continue;

                    // The trigger's latest occurrence is the one in progress, so it cannot yet count for or
                    // against a pattern: confidence is measured over the occurrences that have played out.
                    var completed = _occurrences[trigger] - 1;
                    if (completed <= 0) continue;

                    foreach (var (follower, count) in followers)
                    {
                        // The operator has already acted on this occurrence of the trigger.
                        if (_lastAt.TryGetValue(follower, out var followedAt) && followedAt > _lastAt[trigger]) continue;

                        if (count >= MinSupport && (double)count / completed >= MinConfidence)
                        {
                            result.Add(new SuggestionCandidate(trigger, follower, count, completed));
                        }
                    }
                }

                return result;
            }
        }
    }
}
