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
        private Timer? _topologyTimer;
        private int _refreshing;
        private int _topologyBusy;
        private volatile bool _topologyDirty;
        private string _topologyKey = "";

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

        // The mesh picture. Only fetched while the Network Topology page is open, because it is the biggest response.
        public TopologyResponse? Topology { get; private set; }
        public RouteResponse? Route { get; private set; }
        public string? TopologyError { get; private set; }
        public string? TopologyNotice { get; set; }

        public void Start()
        {
            _timer ??= new Timer(async _ => await RefreshAsync(), null, TimeSpan.Zero, PollInterval);
        }

        public void StartTopology()
        {
            _topologyTimer ??= new Timer(async _ => await RefreshTopologyAsync(), null, TimeSpan.Zero, PollInterval);
        }

        public void StopTopology()
        {
            _topologyTimer?.Dispose();
            _topologyTimer = null;
        }

        /// <summary>
        /// Fetches the mesh for the subnet, root and device currently chosen. If a choice changes while a fetch is
        /// running, the fetch runs again straight away so the screen never keeps showing the old choice.
        /// </summary>
        public async Task RefreshTopologyAsync()
        {
            _topologyDirty = true;
            if (Interlocked.Exchange(ref _topologyBusy, 1) == 1) return;

            try
            {
                while (_topologyDirty)
                {
                    _topologyDirty = false;
                    await FetchTopologyAsync();
                    Changed?.Invoke();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _topologyBusy, 0);
            }
        }

        private async Task FetchTopologyAsync()
        {
            var query = new List<string>();
            if (!string.IsNullOrEmpty(_state.TopologySubnet)) query.Add($"subnet={Uri.EscapeDataString(_state.TopologySubnet)}");
            if (!string.IsNullOrEmpty(_state.TopologyRoot)) query.Add($"root={Uri.EscapeDataString(_state.TopologyRoot)}");

            // Tell the API which version of this same view is already held, so an unchanged mesh costs no download or parse.
            var key = $"{_state.TopologySubnet}|{_state.TopologyRoot}";
            if (Topology is not null && key == _topologyKey) query.Add($"since={Topology.Version}");

            try
            {
                using var response = await _http.GetAsync("api/Topology" + (query.Count > 0 ? "?" + string.Join('&', query) : ""));
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    // The chosen root is unknown or has gone down: say why, go back to the gateway and fetch again.
                    TopologyNotice = (await response.Content.ReadAsStringAsync()).Trim('"') + " Showing connections from the gateway instead.";
                    _state.TopologyRoot = null;
                    _topologyDirty = true;
                    return;
                }

                response.EnsureSuccessStatusCode();
                if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
                {
                    Topology = await response.Content.ReadFromJsonAsync<TopologyResponse>();
                    _topologyKey = key;
                }
                TopologyError = null;
            }
            catch (Exception)
            {
                TopologyError = "Cannot reach the API. Retrying...";
                return;
            }

            if (string.IsNullOrEmpty(_state.TopologyMac))
            {
                Route = null;
                return;
            }

            try
            {
                using var response = await _http.GetAsync($"api/Topology/route/{Uri.EscapeDataString(_state.TopologyMac)}");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // The device was deleted or deactivated since it was chosen.
                    TopologyNotice = $"{_state.TopologyMac} is no longer in the mesh.";
                    _state.TopologyMac = null;
                    Route = null;
                    return;
                }

                response.EnsureSuccessStatusCode();
                Route = await response.Content.ReadFromJsonAsync<RouteResponse>();
            }
            catch (Exception)
            {
                TopologyError = "Cannot reach the API. Retrying...";
            }
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
            _topologyTimer?.Dispose();
        }
    }
}
