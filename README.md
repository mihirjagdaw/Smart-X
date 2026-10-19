# Smart-X

A vineyard IoT telemetry system. Sensors (soil moisture, canopy temperature, power draw, actuators like irrigation valves) report readings to a central API, which validates, queues, stores and analyses them. A Blazor web app lets you register sensors, watch readings arrive in real time, send override commands, and get suggested actions before you go looking for them.

- **Part 1:** sensor registration, telemetry ingestion and live status dashboard.
- **Part 2:** real-time processing (queues, priority alerts), device lookup, command history with undo, alert de-duplication, search/filter, and a predictive suggestion engine.

**Part 1 demo video:** https://youtu.be/EgvMt7jgL68

**Part 2 demo video:** https://youtu.be/-F49rAjtaYs

## Stack

- **Smart-X.Api** — ASP.NET Core Web API + EF Core + SQLite
- **Smart-X.Client** — Blazor WebAssembly
- **Smart-X.Shared** — DTOs and models shared between the two

## Running it

### Option 1: Docker (recommended)

```bash
docker compose up -d
```

Then open **http://localhost:5128**. The API is on **http://localhost:5256**. Data (SQLite DB + uploaded files) persists in `docker-data/` across restarts. After changing any code, rebuild with `docker compose up -d --build`.

```bash
docker compose down
```

On Windows, double-clicking [`docker-setup.bat`](docker-setup.bat) does the same thing without touching a terminal — it builds and starts the stack, waits for the client to actually respond, then opens it in your browser.

### Option 2: Local dev

Run the API and client in separate terminals from the repo root (stop any Docker containers first, they use the same ports):

```bash
dotnet run --project Smart-X.Api
```

```bash
dotnet run --project Smart-X.Client
```

The database is auto-created and migrated on first run — no manual `dotnet ef` steps needed either way.

### Settings

| Setting | Default | What it does |
|---|---|---|
| `Simulator:ApiBaseUrl` | `http://localhost:5256` | Where the built-in device simulator posts its readings. `docker-compose.yml` sets it to `http://localhost:8080` (the API's port inside the container) via `Simulator__ApiBaseUrl`. |
| `Processing:StandardDelayMs` | `100` | Pause after each normal-lane packet, so a backlog is visible in the demo. Critical packets are never delayed. |
| `Monitor:DisconnectAfterSeconds` | `15` | How long a device can stay silent before a Disconnected alert is raised. |

## Features

**Part 1**

- Register and manage sensors (soil moisture, canopy temp, power, actuators), each with a MAC address, location, category and description
- Deactivate / reactivate / hard-delete sensors
- Ingest telemetry readings, with the value type locked to the sensor's category to prevent mismatched data
- Live sensor status dashboard with freshness indicators, auto-refreshing every 5 seconds
- Attach and download files against a sensor (e.g. install photos, logs)

**Part 2**

- Telemetry is queued and processed asynchronously: normal readings in a FIFO queue, critical alerts jump ahead in a priority queue
- The simulator behaves like real devices: it posts readings to the API over HTTP, and the API rejects readings from unregistered devices
- Real-Time Command Stream page: live processing feed, queue lanes, device grid, timeline chart, alerts and commands
- Override commands for actuators (on/off, "shut down all") with a multi-step undo
- Duplicate-free alerts, including automatic Disconnected alerts for devices that go quiet
- Search by words and filter by category or alert state
- Suggested Actions and Automated Insights: learns what you usually do after an alert, and warns when a reading is trending towards the edge of its safe range
- Navigation keeps your place: filters, selections and half-filled forms survive moving between modules

## Part 2: how it fits together

```text
device / simulator ── HTTP ──> POST /api/Telemetry  (or /batch)
                                   │  look the device up by MAC in a Dictionary: unknown -> 404
                                   │  classify the value: Critical or Normal
                                   v
              ┌──────────────────────────┐   ┌───────────────────────────┐
              │ PriorityQueue            │   │ Queue<T> (FIFO)           │
              │ critical alerts, commands│   │ normal telemetry          │
              └────────────┬─────────────┘   └─────────────┬─────────────┘
                           └──── one consumer, priority lane first ────┘
                                              v
                         TelemetryProcessorService
                           ├─ history store: ring buffer + SortedDictionary timeline, and SQLite
                           ├─ AlertTracker (HashSet): raise once, clear on recovery
                           └─ ActionHistory -> InsightEngine: suggestions and warnings
```

The menu has three modules: **Sensor Data Ingestion and Telemetry** (Part 1, with Manage Sensors beneath it), **Real-Time Command Stream and History** (Part 2), and **Network Topology and Mesh Routing** (a placeholder for the final PoE).

## Testing Part 2

Start the system (see [Running it](#running-it)), then register some test devices. In PowerShell, once:

```powershell
$api = "http://localhost:5256/api"
$devices = @(
    @{ macAddress = "AA:00:00:00:00:01"; location = "Estate/Block B";    category = 0; description = "Moisture Sensor B" },
    @{ macAddress = "AA:00:00:00:00:02"; location = "Estate/Block C";    category = 0; description = "Canopy Temp" },
    @{ macAddress = "AA:00:00:00:00:03"; location = "Estate/Pump House"; category = 1; description = "Pump Power" },
    @{ macAddress = "AA:00:00:00:00:04"; location = "Estate/Pump House"; category = 2; description = "Pump A" },
    @{ macAddress = "AA:00:00:00:00:05"; location = "Estate/Pump House"; category = 2; description = "Valve 7" }
)
foreach ($d in $devices) {
    Invoke-RestMethod -Method Post -Uri "$api/Sensors" -ContentType "application/json" -Body ($d | ConvertTo-Json) | Out-Null
}
```

(Categories: `0` Environmental, `1` PowerConsumption, `2` Actuator.) You can also register them on the Ingestion page. Then open **Real-Time Command Stream and History**. Click **View** on a device to select it: its timeline appears, along with **Simulation tools** that make that device behave in specific ways.

### 1. Simulate a high-priority alert

A reading is critical when an Environmental value is below 20 or above 80, or a Power value is above 3000.

- **In the app:** select **Moisture Sensor B** and click **Send a critical reading**.
- **Or by API:**

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5256/api/Telemetry -ContentType "application/json" -Body '{"deviceId":"AA:00:00:00:00:01","category":0,"valueType":"float","floatValue":5}'
Invoke-RestMethod http://localhost:5256/api/Alerts
```

**Expect:** the reading appears in the **Processing feed** on the red **Priority** lane, a Critical alert is raised, the device row is flagged, and the menu item shows a red alert count. **Send a normal reading** clears it.

### 2. See the priority queue overtake a backlog (Queue + PriorityQueue)

Select **Moisture Sensor B** and click **Burst: 40 normal + 1 critical**. This posts 41 readings in one batch; the critical one is sent *last*.

**Expect:** scroll the Processing feed (newest first) to the red **Priority** row. It was processed ahead of all 40 grey **Standard** rows: it waited only milliseconds, while the normal packets waited up to about 4 seconds in the FIFO queue (they are processed in the order they arrived). The **Ingest queues** panel shows each lane's waiting and processed counts, also available at `http://localhost:5256/api/Stream/stats`.

### 3. Dictionary lookups reject unknown devices

Every incoming packet is checked against the device registry, a `Dictionary` keyed by MAC address, so the lookup is O(1).

```powershell
try { Invoke-RestMethod -Method Post -Uri http://localhost:5256/api/Telemetry -ContentType "application/json" -Body '{"deviceId":"NO:SUCH:DEVICE","category":0,"valueType":"float","floatValue":1}' } catch { [int]$_.Exception.Response.StatusCode }
```

**Expect:** `404`. A deactivated device is rejected with `400`, and a batch accepts the valid readings while listing the rejected ones.

### 4. Ordered history (SortedDictionary)

Each device keeps its recent readings (up to 500) in a `SortedDictionary` keyed by timestamp, so timelines come out already in order.

```powershell
Invoke-RestMethod "http://localhost:5256/api/Telemetry/AA:00:00:00:00:01/timeline?minutes=5"
```

**Expect:** readings in ascending timestamp order. In the app, the chart under the device table draws the same data; change its window with the selector (1, 5 or 30 minutes).

### 5. Unique alerts (HashSet)

Select **Canopy Temp**, click **Toggle simulated offline** and wait about 17 seconds.

**Expect:** a single **Disconnected** alert, while **Duplicate alerts suppressed** keeps counting up every couple of seconds (the same condition is detected repeatedly, but the set only holds it once). Click the button again: the device reports on its next tick and the alert clears. The same applies to repeated critical readings: only the first raises an alert.

### 6. Command history and undo (Stack)

1. Click **On** next to **Pump A**, then **On** next to **Valve 7**.
2. Click **Shut down all actuators**.
3. Click **Undo last command**.

**Expect:** the Commands list shows the most recent command at the top, marked "next to undo". Undoing the shutdown puts both devices back to ON in a single step. Keep clicking **Undo** and it works back through the history until the list is empty and the button disables.

### 7. Search and filter

Type `pump a` in the search box; it narrows the table to Pump A. Words match the start of words in a device's description or location (`pump` also finds everything in "Pump House"). Use the category and alert-state dropdowns to narrow further, and **Clear** to reset.

```powershell
Invoke-RestMethod "http://localhost:5256/api/Devices/search?q=pump&category=Actuator&record=false"
```

(`record=false` keeps test queries out of what the suggestion engine learns.)

### 8. Suggested Actions and Automated Insights

**Teach it a habit.** Select **Moisture Sensor B**, then repeat these steps **3 times**:

1. **Send a critical reading**
2. Type `pump a` in the search box and wait a second
3. Click **On** next to Pump A
4. Click **Undo last command** (undo is not learned)
5. Click **Clear**, then **Send a normal reading**

On the 4th time, just click **Send a critical reading**. **Expect:** **Suggested Actions** offers **Check Pump A** (and **Turn ON Pump A**, if the pump is currently off) with a confidence such as "3 of 3 times (100%)". Click **Apply**: the command runs, or the device is searched for and selected, and the suggestion goes away because you have now acted on it. If "Turn ON Pump A" is missing, the simulator already has the pump on; click **Off** and it appears.

**Spot trouble early.** Select **Canopy Temp** and click **Falling trend (12 readings)**. **Expect:** **Automated Insights** warns that it is trending down and predicted to leave the safe range, before a single reading is actually out of range. A sudden spike (for example **Send a critical reading** on a power meter that normally sits near 1200) shows an "Unusual jump" warning.

### 9. Automated checks

```bash
dotnet run test-part2.cs
```

Runs assertions over the registry, both queue lanes, the sorted timeline, the alert set, the command stack and the suggestion engine (replaying operator behaviour with a fake clock, so hours of habit run instantly). **Expect:** every line `PASS` and `All checks passed.` Part 1's recursion check is `dotnet run test-deployment-node.cs`.

### API summary (Part 2)

| Endpoint | Purpose |
|---|---|
| `POST /api/Telemetry`, `POST /api/Telemetry/batch` | Queue one reading, or up to 500 (`202 Accepted`) |
| `GET /api/Telemetry/{mac}/timeline?minutes=` | Time-ordered readings for a device |
| `GET /api/Stream/stats`, `GET /api/Stream/snapshot` | Queue depths and processed feed; everything the Command Stream page shows in one response |
| `GET /api/Alerts` | Active alerts, duplicates suppressed, recent alert log |
| `POST /api/Devices/{mac}/simulate-offline` | Testing hook: make the simulator stop or resume reporting for a device |
| `GET /api/Devices/search?q=&category=&alert=` | Search and filter active devices |
| `POST /api/Commands`, `POST /api/Commands/all-off`, `POST /api/Commands/undo`, `GET /api/Commands` | Override commands, bulk shutdown, undo, history |
| `GET /api/Insights` | Suggested actions and device warnings |

## Technical requirements

### Part 1

A few language/CLR features the brief calls for, and where they live:

- **Generics** — [`TelemetryPacket<T>`](Smart-X.Shared/TelemetryPacket.cs) wraps a single reading with `T : struct`, so `float`, `int`, and `bool` payloads are stored and read as their real value type instead of boxing everything to `object`. `TelemetryController` picks the concrete `T` based on the sensor's category before handing the packet off to be normalized into a `SensorReading`.

- **Operator overloading** — [`SensorReading`](Smart-X.Shared/SensorReading.cs) overloads `+`, `-`, `<`, and `>` so readings can be compared and combined directly instead of reaching into `.Value` everywhere. `TelemetryHistoryStore.GetTrend` (`GET /api/Telemetry/{deviceId}/trend`) uses `-`, `<`, and `>` to compute the delta and direction between a sensor's two most recent readings; `GetCategoryTotal` (`GET /api/Telemetry/total/{category}`) uses `+` to sum the latest reading across every sensor in a category. Both surface directly in the Ingestion page's Sensor Status Overview — a Rising/Falling/Stable badge per sensor and a category totals line. Mismatched categories or device IDs throw rather than silently producing a meaningless result.

- **Recursion** — [`DeploymentNode.ValidateNode`](Smart-X.Shared/DeploymentNode.cs) recursively walks a facility → zone → subzone → device hierarchy, failing the whole subtree if any child node has a blank name. `SensorsController` builds this tree from a sensor's `Location` field (a `/`-delimited path like `Estate/Block A/Row 3`, via `DeploymentNode.FromPath`) and rejects registration or updates that don't validate — so a location like `Estate//Row 3` is caught server-side instead of silently stored.

- **Custom data structure** — [`SensorHistoryBuffer`](Smart-X.Api/SensorHistoryBuffer.cs) is a fixed-capacity circular buffer: `Add` is O(1) and once full, the oldest reading is overwritten rather than the array growing forever. Each sensor gets one of these in `TelemetryHistoryStore`, which keeps the latest-reading and trend lookups fast no matter how long a sensor has been reporting.

- **Jagged arrays** — [`MockTelemetryGenerator.GenerateBatches`](Smart-X.Api/MockTelemetryGenerator.cs) returns a `float[][]` where each row (device) has a different number of readings. `SensorsController.RegisterSensor` calls it to seed a newly registered Environmental sensor with a handful of plausible historical readings, so its history/dashboard isn't empty before the background simulator has ticked.

### Part 2

- **Queue (FIFO)** — [`TelemetryPipeline`](Smart-X.Api/Processing/TelemetryPipeline.cs) holds normal telemetry in a `Queue<Packet>`, so readings are processed in the order they arrived. A `SemaphoreSlim` wakes the single consumer, [`TelemetryProcessorService`](Smart-X.Api/Processing/TelemetryProcessorService.cs), the moment something is queued, with no polling.

- **Priority queue** — the same class holds critical alerts and operator commands in a `PriorityQueue<Packet, (int Rank, long Sequence)>`, and the consumer always drains it before touching the FIFO queue, so an alert never waits behind a backlog. `PriorityQueue` does not keep arrival order for equal priorities, so a sequence number is part of the priority key; that keeps critical packets first-come-first-served among themselves.

- **Stack** — [`CommandHistory`](Smart-X.Api/Processing/CommandHistory.cs) pushes every override command onto a `Stack<CommandRecord>`. Each record stores the previous state of every device it touched, so popping the top reverts exactly the last command, including a bulk "shut down all actuators", in one step.

- **Dictionary** — [`DeviceRegistry`](Smart-X.Api/Processing/DeviceRegistry.cs) is a `Dictionary<string, DeviceState>` keyed by MAC address (case-insensitive), kept in sync by the sensor controller and loaded from the database at startup. Each incoming packet is an O(1) lookup instead of a database query, and the registry (not the request body) is the source of truth for a device's category. [`ActionHistory`](Smart-X.Api/Processing/ActionHistory.cs) also uses a `Dictionary` of `Dictionary`s to count which action follows which.

- **Sorted dictionary** — [`TelemetryHistoryStore`](Smart-X.Api/TelemetryHistoryStore.cs) keeps a `SortedDictionary<DateTimeOffset, SensorReading>` per device (capped at 500, oldest dropped), so `GET /api/Telemetry/{mac}/timeline` and the dashboard chart read readings already in time order. Two readings with an identical timestamp are both kept by nudging the key one tick.

- **Hash set** — [`AlertTracker`](Smart-X.Api/Processing/AlertTracker.cs) stores active alerts in a `HashSet<AlertKey>` of (device, alert type). `Add` returns `false` for something already present, which is what stops a persistent problem, such as a node that stays offline, from raising an alert on every check; those repeats are counted and shown as "duplicates suppressed". [`DisconnectMonitorService`](Smart-X.Api/Processing/DisconnectMonitorService.cs) (a `PeriodicTimer` loop) raises Disconnected alerts, and [`SimulatorControl`](Smart-X.Api/Processing/SimulatorControl.cs) uses a `HashSet` of devices to pretend are unplugged.

- **Predictive suggestions** — [`ActionHistory`](Smart-X.Api/Processing/ActionHistory.cs) records alerts, searches and commands, and counts how often each action follows another within two minutes (a first-order transition model). [`InsightEngine`](Smart-X.Api/Processing/InsightEngine.cs) looks at the alerts active right now and the operator's last few actions, and suggests the follow-ups that happened in at least two earlier occurrences and at least half of them, ranked by confidence using a `PriorityQueue`. It never re-suggests something already done or a command that would leave a device where it already is.

- **Automated insights** — the same engine reads each device's sorted timeline to warn about trouble early: a least-squares line over the latest readings forecasts when a clean trend will leave the safe range (within ten minutes), and a reading that moves more than four times its normal step is flagged as an unusual jump.

- **State across navigation** — [`AppState`](Smart-X.Client/Services/AppState.cs) lives for the life of the app, so filters, the selected device and unsent forms survive moving between modules, and [`LiveDataService`](Smart-X.Client/Services/LiveDataService.cs) runs a single poller that every page and the menu's alert badge read from, so leaving a page never stops or restarts fetching.

## References

- [How do I convert int/decimal to float in C#?](https://stackoverflow.com/questions/1042099/how-do-i-convert-int-decimal-to-float-in-c) — normalizing `TelemetryPacket<T>` values to `float` in `TelemetryController`
- [Most efficient way to save a byte array as a file on disk in C#](https://stackoverflow.com/questions/128674/what-is-the-most-efficient-way-to-save-a-byte-array-as-a-file-on-disk-in-c) — streaming uploaded files to disk in `SensorsController.UploadSensorFile`
- [Background tasks with hosted services in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services) — `BackgroundService` base class used by the simulator, processor and disconnect monitor
- [Operator overloading — C# reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/operator-overloading) — the `+`, `-`, `<`, `>` overloads on `SensorReading`
- [Applying migrations — EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) — `Database.Migrate()` auto-applying migrations on startup in `Program.cs`
- [ASP.NET Core Blazor file uploads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-uploads) — `InputFile`/`IBrowserFile` usage in the Attach Files section of `Ingestion.razor`
- [Queue&lt;T&gt; Class](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.queue-1?view=net-10.0) — the FIFO lane in `TelemetryPipeline`
- [PriorityQueue&lt;TElement,TPriority&gt; Class](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.priorityqueue-2?view=net-10.0) — the critical-alert lane in `TelemetryPipeline` and the top-k ranking in `InsightEngine` (its documentation notes equal priorities are not served first-in-first-out, hence the sequence number in the key)
- [Stack&lt;T&gt; Class](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.stack-1?view=net-10.0) — undo history in `CommandHistory`
- [Dictionary&lt;TKey,TValue&gt; Class](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2?view=net-10.0) — the MAC-keyed `DeviceRegistry` and the transition counts in `ActionHistory`
- [SortedDictionary&lt;TKey,TValue&gt; Class](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.sorteddictionary-2?view=net-10.0) — the per-device timeline in `TelemetryHistoryStore`
- [HashSet&lt;T&gt; Class](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.hashset-1) — active alerts in `AlertTracker` and simulated-offline devices in `SimulatorControl`
- [PeriodicTimer Class](https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer?view=net-10.0) — the check loop in `DisconnectMonitorService`
- [ASP.NET Core Blazor dependency injection](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/dependency-injection?view=aspnetcore-10.0) — app-wide services (`AppState`, `LiveDataService`) in the client's `Program.cs`

## Project layout

```text
Smart-X.Api/
  Controllers/       Sensors, Telemetry, Stream, Alerts, Devices, Commands, Insights
  Processing/        Pipeline (queues), registry, alert tracker, command history, insight engine, background services
  Data/, Migrations/ EF Core model and migrations
Smart-X.Client/
  Pages/             Home, Ingestion, Manage Sensors, Command Stream, Topology (placeholder)
  Components/        Timeline chart
  Services/          AppState, LiveDataService
  Layout/            Main layout and navigation menu
Smart-X.Shared/      DTOs and domain models used by both projects
test-part2.cs        Self-check script for the Part 2 data structures and engine
```
