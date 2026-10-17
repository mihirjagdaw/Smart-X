#:project Smart-X.Api/Smart-X.Api.csproj
using Smart_X.Api;
using Smart_X.Api.Data;
using Smart_X.Api.Processing;
using Smart_X.Shared;

int failures = 0;

void Check(string label, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label}{(ok || detail == "" ? "" : "  -> " + detail)}");
    if (!ok) failures++;
}

Packet Make(string mac, float value, PacketRank rank) =>
    new(mac, SensorCategory.Environmental, value, DateTimeOffset.UtcNow, rank);

// --- DeviceRegistry: MAC lookups are case-insensitive, entries come back as snapshots ---
var registry = new DeviceRegistry();
registry.Upsert(new SensorProfile(0, "AA:BB:CC:01", "Estate/Block A", SensorCategory.Environmental, "Moisture", DateTimeOffset.UtcNow, true));
Check("registry finds a MAC regardless of case", registry.TryGet("aa:bb:cc:01", out var found) && found.Mac == "AA:BB:CC:01");
Check("registry misses an unknown MAC", !registry.TryGet("FF:FF:FF:FF", out _));
registry.Update("AA:BB:CC:01", d => d with { LastValue = 42f });
Check("registry update is visible to the next reader", registry.TryGet("AA:BB:CC:01", out var updated) && updated.LastValue == 42f);
registry.Remove("AA:BB:CC:01");
Check("registry removes a device", !registry.TryGet("AA:BB:CC:01", out _));

// --- TelemetryPipeline: normal packets stay FIFO, a late critical packet overtakes the backlog ---
var pipeline = new TelemetryPipeline();
for (var i = 1; i <= 5; i++) pipeline.Enqueue(Make("N", i, PacketRank.Normal));
pipeline.Enqueue(Make("C1", 99, PacketRank.Critical));
pipeline.Enqueue(Make("C2", 98, PacketRank.Critical));

var order = new List<string>();
for (var i = 0; i < 7; i++)
{
    var (packet, lane) = await pipeline.DequeueAsync(CancellationToken.None);
    order.Add($"{packet.Mac}:{packet.Value}:{lane}");
}

Check("critical packets dequeue first, in arrival order",
    order[0] == "C1:99:Priority" && order[1] == "C2:98:Priority", string.Join(" ", order));
Check("normal packets keep FIFO order behind them",
    string.Join(",", order.Skip(2)) == "N:1:Standard,N:2:Standard,N:3:Standard,N:4:Standard,N:5:Standard", string.Join(" ", order));

pipeline.Enqueue(Make("N", 6, PacketRank.Normal));
pipeline.Enqueue(Make("C3", 97, PacketRank.Critical));
var stats = pipeline.GetStats();
Check("stats report per-lane depth", stats.StandardDepth == 1 && stats.PriorityDepth == 1, $"{stats.StandardDepth}/{stats.PriorityDepth}");

// --- TelemetryHistoryStore timeline: ordered on insert, collision-safe, bounded, windowed ---
var store = new TelemetryHistoryStore();
var start = DateTimeOffset.UtcNow.AddMinutes(-10);
SensorReading At(string mac, float value, DateTimeOffset when) => new(mac, SensorCategory.Environmental, value, when);

store.Add(At("T1", 3, start.AddSeconds(3)));
store.Add(At("T1", 1, start.AddSeconds(1)));
store.Add(At("T1", 2, start.AddSeconds(2)));
Check("timeline returns out-of-order inserts sorted by timestamp",
    string.Join(",", store.GetTimeline("T1", 60).Select(r => r.Value)) == "1,2,3");

store.Add(At("T2", 10, start));
store.Add(At("T2", 20, start));
Check("two readings with the same timestamp are both kept", store.GetTimeline("T2", 60).Count == 2);

for (var i = 0; i < 600; i++) store.Add(At("T3", i, start.AddMilliseconds(i)));
var capped = store.GetTimeline("T3", 60);
Check("timeline is capped at 500 entries, dropping the oldest",
    capped.Count == 500 && capped[0].Value == 100 && capped[^1].Value == 599, $"{capped.Count} first={capped[0].Value}");

store.Add(At("T4", 1, DateTimeOffset.UtcNow.AddMinutes(-120)));
store.Add(At("T4", 2, DateTimeOffset.UtcNow.AddMinutes(-1)));
Check("timeline window excludes readings older than the cutoff",
    string.Join(",", store.GetTimeline("T4", 60).Select(r => r.Value)) == "2");

store.RemoveDevice("T1");
Check("removing a device clears its timeline", store.GetTimeline("T1", 60).Count == 0);
Check("an unknown device has an empty timeline", store.GetTimeline("NOPE", 60).Count == 0);

// --- AlertTracker: a HashSet keeps one alert per (device, type) however often it is reported ---
var alerts = new AlertTracker();
Check("first raise of an alert is new", alerts.TryRaise("A1", AlertType.Disconnected, "offline"));
Check("a repeat raise is suppressed", !alerts.TryRaise("A1", AlertType.Disconnected, "offline"));
alerts.TryRaise("A1", AlertType.Disconnected, "offline");
var snap = alerts.Snapshot();
Check("one active alert and two duplicates suppressed",
    snap.Active.Count == 1 && snap.Suppressed == 2, $"{snap.Active.Count} active, {snap.Suppressed} suppressed");
Check("the same device can hold a different alert type", alerts.TryRaise("A1", AlertType.Critical, "too high"));
Check("clearing an alert that is not active does nothing", !alerts.Clear("A2", AlertType.Critical, "ok"));
Check("clearing an active alert succeeds", alerts.Clear("A1", AlertType.Disconnected, "reconnected"));
Check("a cleared alert can be raised again", alerts.TryRaise("A1", AlertType.Disconnected, "offline again"));
alerts.ClearDevice("a1");
Check("clearing a device removes all its alerts, whatever the MAC casing", alerts.Snapshot().Active.Count == 0);
Check("the log records raises and clears, newest first",
    alerts.Snapshot().Recent[0].Raised == false && alerts.Snapshot().Recent.Count(e => e.Raised) == 3);

// --- SimulatorControl: toggling reports the new state ---
var control = new SimulatorControl();
Check("first toggle takes a device offline", control.ToggleOffline("A1") && control.IsOffline("a1"));
Check("second toggle brings it back", !control.ToggleOffline("A1") && !control.IsOffline("A1"));

// --- Pipeline ranks: critical, then operator commands, then normal telemetry ---
var ranked = new TelemetryPipeline();
ranked.Enqueue(Make("NORMAL", 1, PacketRank.Normal));
ranked.Enqueue(Make("COMMAND", 1, PacketRank.Command));
ranked.Enqueue(Make("CRITICAL", 1, PacketRank.Critical));
var rankOrder = new List<string>();
for (var i = 0; i < 3; i++) rankOrder.Add((await ranked.DequeueAsync(CancellationToken.None)).Packet.Mac);
Check("critical beats command beats normal", string.Join(",", rankOrder) == "CRITICAL,COMMAND,NORMAL", string.Join(",", rankOrder));

// --- CommandHistory: a Stack, so undo always reverts the most recent command first ---
var history = new CommandHistory();
Check("undo on an empty history returns nothing instead of crashing", history.TryPop() is null);

history.Push("Turn ON Valve 1", new List<DeviceChange> { new("V1", null, 0f) });
var bulk = history.Push("Shut down all", new List<DeviceChange> { new("V1", 1f, 1f), new("V2", null, 1f), new("P1", null, 0f) });
Check("command ids increase", bulk.Id == 2);
Check("history lists the most recent command first", history.Snapshot()[0].Description == "Shut down all");

var undone = history.TryPop();
Check("undo returns the most recent command", undone is not null && undone.Id == 2);
Check("a bulk command reverts every device it touched with its previous state",
    undone!.Changes.Count == 3 && undone.Changes[0] == new DeviceChange("V1", 1f, 1f));
Check("the earlier command is next", history.TryPop()?.Description == "Turn ON Valve 1");
Check("history is empty again", history.TryPop() is null && history.Snapshot().Count == 0);

Console.WriteLine();
Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) FAILED.");
return failures == 0 ? 0 : 1;
