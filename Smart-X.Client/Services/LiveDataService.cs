using System.Net.Http.Json;
using Smart_X.Shared.DTOs;

namespace Smart_X.Client.Services
{
    /// <summary>
    /// One poller for the whole app. Pages and the nav menu read the latest data from here and subscribe to
    /// Changed, so leaving a page never stops or restarts fetching and a page that opens again shows the
    /// last data straight away instead of an empty screen.
    /// </summary>
    public class LiveDataService : IDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        private readonly HttpClient _http;
        private readonly AppState _state;
        private Timer? _timer;
        private int _refreshing;

        public LiveDataService(HttpClient http, AppState state)
        {
            _http = http;
            _state = state;
        }

        public StreamSnapshot? Snapshot { get; private set; }
        public List<DeviceSummary> Results { get; private set; } = new();
        public List<SensorReadingResponse> Timeline { get; private set; } = new();
        public DateTimeOffset? UpdatedAt { get; private set; }
        public string? Error { get; private set; }

        public event Action? Changed;

        public void Start()
        {
            _timer ??= new Timer(async _ => await RefreshAsync(), null, TimeSpan.Zero, PollInterval);
        }

        public async Task RefreshAsync()
        {
            // A slow response must not stack up behind the next tick.
            if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;

            try
            {
                Snapshot = await _http.GetFromJsonAsync<StreamSnapshot>("api/Stream/snapshot");
                Results = await SearchAsync(record: false);
                Timeline = await LoadTimelineAsync();
                UpdatedAt = DateTimeOffset.Now;
                Error = null;
            }
            catch (Exception)
            {
                Error = "Cannot reach the API. Retrying...";
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }

            Changed?.Invoke();
        }

        // The operator changed the search or a filter, so this search is remembered for the insight engine.
        public async Task ApplyFilterAsync()
        {
            try
            {
                Results = await SearchAsync(record: true);
                Error = null;
            }
            catch (Exception)
            {
                Error = "Cannot reach the API. Retrying...";
            }

            Changed?.Invoke();
        }

        public async Task SelectDeviceAsync(string? mac)
        {
            _state.SelectedMac = mac;

            try
            {
                Timeline = await LoadTimelineAsync();
            }
            catch (Exception)
            {
                Timeline = new();
            }

            Changed?.Invoke();
        }

        private async Task<List<DeviceSummary>> SearchAsync(bool record)
        {
            var query = new List<string> { $"record={(record ? "true" : "false")}" };
            if (!string.IsNullOrWhiteSpace(_state.Query)) query.Add($"q={Uri.EscapeDataString(_state.Query.Trim())}");
            if (_state.Category is { } category) query.Add($"category={category}");
            if (_state.Alert is { } alert) query.Add($"alert={alert}");

            return await _http.GetFromJsonAsync<List<DeviceSummary>>($"api/Devices/search?{string.Join('&', query)}") ?? new();
        }

        private async Task<List<SensorReadingResponse>> LoadTimelineAsync()
        {
            if (string.IsNullOrEmpty(_state.SelectedMac)) return new();

            return await _http.GetFromJsonAsync<List<SensorReadingResponse>>(
                $"api/Telemetry/{Uri.EscapeDataString(_state.SelectedMac)}/timeline?minutes={_state.TimelineMinutes}") ?? new();
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
