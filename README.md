# Smart-X

A vineyard IoT telemetry system. Sensors (soil moisture, canopy temperature, power draw, actuators like irrigation valves) report readings to a central API, which validates, queues, stores and analyses them. A Blazor web app lets you register sensors, watch readings arrive in real time, send override commands, and get suggested actions before you go looking for them.

- **Part 1:** sensor registration, telemetry ingestion and live status dashboard.
- **Part 2:** real-time processing (queues, priority alerts), device lookup, command history with undo, alert de-duplication, search/filter, and a predictive suggestion engine.
- **Part 3:** network topology and mesh routing: a live map of how devices relay readings to the gateway, a power-efficient routing backbone (minimum spanning tree), route tracking by MAC address, self-healing when devices fail, and a historical telemetry log backed by a self-balancing (AVL) tree with fast range queries.

**Part 1 demo video:** https://youtu.be/EgvMt7jgL68

**Part 2 demo video:** https://youtu.be/-F49rAjtaYs

**Part 3 demo video:** *(add the YouTube link here)*

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

### Option 2: From a fresh clone with the .NET SDK

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Git. Docker is optional. Run everything from the repo root.

**1. Get the code and restore its packages**

```bash
git clone <repository-url> Smart-X
cd Smart-X
dotnet restore Smart-X.slnx
```

**2. Compile** (expect `0 Warning(s)` and `0 Error(s)`)

```bash
dotnet build Smart-X.slnx
```

**3. Boot the API** (leave it running; it listens on http://localhost:5256)

```bash
dotnet run --project Smart-X.Api
```

The database (`smartx.db`) is created and migrated on first run, and the history archive is rebuilt from it at startup. No `dotnet ef` steps are needed. Swagger is at http://localhost:5256/swagger in the Development environment.

**4. Run the client** (in a second terminal; open http://localhost:5128)

```bash
dotnet run --project Smart-X.Client
```

Stop any Docker containers first: they use the same ports. The built-in simulator needs registered devices before it has anything to send, so follow [Testing Part 3](#testing-part-3) to create a demo estate.

### Settings

| Setting | Default | What it does |
|---|---|---|
| `Simulator:ApiBaseUrl` | `http://localhost:5256` | Where the built-in device simulator posts its readings. `docker-compose.yml` sets it to `http://localhost:8080` (the API's port inside the container) via `Simulator__ApiBaseUrl`. |
| `Processing:StandardDelayMs` | `100` | Pause after each normal-lane packet, so a backlog is visible in the demo. Critical packets are never delayed. **This caps ingest at about 10 readings a second; set it to `0` for more than about 50 devices** (see [Scaling evidence](#scaling-evidence)). |
| `Monitor:DisconnectAfterSeconds` | `15` | How long a device can stay silent before a Disconnected alert is raised. |
| `History:RetentionDays` | `30` | How long readings are kept, in the database and in the in-memory history trees. Each device reporting every 5 seconds adds about 1 MB of memory and 2.4 MB of disk per day, so lower this for large fleets (7 or less above about 100 devices). |
| `Mesh:SpacingMetres` | `40` | Distance between neighbouring devices when the map is laid out. |
| `Mesh:RadioRangeMetres` | `80` | Two devices closer than this can talk to each other directly. |
| `Mesh:ElectronicsNjPerBit` | `50` | Energy to run the radio electronics, per bit, at each end of a link. |
| `Mesh:AmplifierNjPerBitPerSquareMetre` | `0.1` | Extra transmit energy per bit per square metre of distance. |

Any setting can be overridden by an environment variable with `__` for the colon, for example `Processing__StandardDelayMs=0`.

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

**Part 3**

- A live **mesh map**: every device placed by its location, linked to the neighbours it can reach by radio, coloured by status (online, critical reading, down, isolated)
- A **power-efficient backbone**: the minimum spanning tree of the mesh (Prim's algorithm, checked against Kruskal's), with the energy it saves against using every link
- **Route tracking by MAC address**: pick a device and see its route to the gateway hop by hop, the cheapest possible route (Dijkstra) for comparison, the backup route if its first hop failed, and its neighbours
- **Who is connected?** Trace connections from the gateway or from any device (breadth-first search), and filter to a subnet (a facility or zone) to see how many of its devices are connected, down or cut off
- **Self-healing log**: when a device goes silent the mesh re-routes around it, and the log lists what went down, what was cut off and which routes moved
- **Historical telemetry log** for any device: pick a time window (presets or a custom range), page through readings newest or oldest first, and see a chart of the page. It is served from a self-balancing AVL tree that answers a range query in microseconds
- Menu integration: the Topology link shows a red badge when devices are down, and **Show on mesh map** / **Open in command stream** link a device between the Command Stream and Topology pages

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
                           └─ alerts, searches and commands -> ActionFeed (Channel)
                                  -> PredictionService (background): ActionHistory (Queue) -> InsightEngine
                                  -> latest suggestions and warnings, read by the API
```

The menu has three modules: **Sensor Data Ingestion and Telemetry** (Part 1, with Manage Sensors beneath it), **Real-Time Command Stream and History** (Part 2), and **Network Topology and Mesh Routing** (Part 3).

## Part 3: how it fits together

```text
registered devices (DeviceRegistry) + active alerts (AlertTracker)
              │  once a second: TopologyService (BackgroundService)
              v
 MeshLayout    place each device from its location path (zone grid, repeatable jitter from the MAC)
 MeshBuilder   link devices within radio range, weight = energy per bit; join separate islands by "backhaul" links
              │  only rebuilt when a device is added, removed or moved
              v
 TopologyAnalyzer   remove devices that are down
                    BFS from the gateway ........ who is reachable, in how many hops; who is cut off
                    Prim from the gateway ....... the power-efficient backbone (Kruskal re-checks the total)
                    walk the backbone ........... each device's next hop, hops and cost to the gateway
                    Dijkstra from the gateway ... each device's cheapest possible route, for comparison
                    Diff against the last result  -> self-healing log (Down, CutOff, Rerouted, Joined, ...)
              │  cached; only recomputed when the set of devices, down devices or critical devices changes
              v
 GET api/Topology, GET api/Topology/route/{mac}  ─────>  Blazor client (polls only while the page is open)

 processor ──> TelemetryArchive: one AvlTree per device, keyed by timestamp ──> GET api/History/{mac}
               (also written to SQLite, and reloaded from it at startup)
```

### The data structures and algorithms

Everything below is plain C# with no graph or tree library. Each piece has its own test script (see [Testing Part 3](#testing-part-3)).

#### Self-balancing tree: an AVL tree with subtree sizes ([`AvlTree.cs`](Smart-X.Api/History/AvlTree.cs))

**What it stores.** Every reading a device has sent, keyed by timestamp, one tree per device ([`TelemetryArchive`](Smart-X.Api/History/TelemetryArchive.cs)). The history log asks it questions such as "give me readings 26 to 50 between 14:00 and 15:00".

**Why a self-balancing tree.** Timestamps arrive in increasing order. In a plain binary search tree that order produces a lopsided tree: a linked list with O(n) lookups. An AVL tree keeps the two subtrees of every node within one level of each other, repairing the balance with rotations after each insert or removal, so its height stays below about 1.44 × log₂(n) and every operation is O(log n) whatever order the keys arrive in. I chose AVL over a red-black tree because its tighter balance makes lookups and range queries slightly faster, which suits a log that is read far more often than it is changed.

**The range-query trick.** Each node also stores the size of its subtree. That makes "how many keys are less than *t*" an O(log n) walk (whenever the search turns right, add the left subtree's size without visiting it), so the number of readings in a window is two such walks subtracted, and a page of results can start at any position without stepping over the earlier ones:

```csharp
// Number of keys strictly less than "key": skip whole left subtrees by their stored size.
public int CountLess(TKey key)
{
    var count = 0;
    for (var node = _root; node is not null;)
    {
        if (key.CompareTo(node.Key) <= 0) node = node.Left;
        else { count += 1 + SizeOf(node.Left); node = node.Right; }
    }
    return count;
}

// One page of the readings between from and to: find the start in O(log n), then walk on in order.
public IEnumerable<KeyValuePair<TKey, TValue>> Between(TKey from, TKey to, int skip, int take)
{
    var inRange = CountBetween(from, to);
    return Slice(CountLess(from) + skip, Math.Min(take, inRange - skip));
}
```

The balance is repaired on the way back up from every insert and removal:

```csharp
private static Node Rebalance(Node node)
{
    Refresh(node);                                   // height and size from the children
    var lean = HeightOf(node.Left) - HeightOf(node.Right);
    if (lean > 1)                                    // left side too tall
    {
        if (HeightOf(node.Left!.Left) < HeightOf(node.Left.Right)) node.Left = RotateLeft(node.Left);
        return RotateRight(node);
    }
    if (lean < -1)                                   // right side too tall
    {
        if (HeightOf(node.Right!.Right) < HeightOf(node.Right.Left)) node.Right = RotateRight(node.Right);
        return RotateLeft(node);
    }
    return node;
}
```

| Operation | Cost | Where it is used |
|---|---|---|
| Insert a reading | O(log n) | the processor, for every accepted reading |
| Remove a reading | O(log n) | retention (drop readings older than `History:RetentionDays`) |
| Count readings in a window | O(log n) | the "total" shown above the history table |
| Fetch one page of a window | O(log n + page size) | `GET api/History/{mac}` |

Around it: the archive keeps one tree per device behind one lock, nudges a duplicate timestamp by one tick so no reading is lost, is reloaded from SQLite at startup, and is trimmed hourly by [`HistoryRetentionService`](Smart-X.Api/History/HistoryRetentionService.cs). The API response includes the tree's size, height and lookup time, and the history panel shows them.

#### Graph, breadth-first and depth-first search ([`MeshGraph.cs`](Smart-X.Api/Network/MeshGraph.cs), [`GraphTraversal.cs`](Smart-X.Api/Network/GraphTraversal.cs))

The mesh is an undirected weighted graph stored as an adjacency list (a dictionary from each device to its neighbours and link costs), because a mesh is sparse: each device only reaches a handful of neighbours, so a list takes O(V + E) memory where a matrix would take O(V²). Vertices are devices plus the gateway; an edge is a radio link weighted by its energy cost per bit.

- **BFS** (a `Queue`) finds the fewest hops from a start device to every other device. The mesh uses it for "who is connected?", to trace connections from the gateway or from any chosen device, and to find devices that are cut off when relays fail.
- **DFS** is written iteratively with an explicit `Stack`, so a chain of 20,000 devices cannot overflow the call stack (a test builds exactly that chain).
- **Connected components** run BFS from each unvisited device. The mesh builder uses them to find separate islands of devices that cannot hear each other, and the subnet report uses them to count the islands inside a subnet.

```csharp
while (queue.Count > 0)
{
    var current = queue.Dequeue();
    order.Add(current);
    foreach (var (neighbour, _) in graph.Neighbors(current))
    {
        if (hops.ContainsKey(neighbour)) continue;   // already seen: the hops entry doubles as the visited marker
        hops[neighbour] = hops[current] + 1;
        parent[neighbour] = current;
        queue.Enqueue(neighbour);
    }
}
```

All three run in O(V + E).

#### Power-efficient routing: minimum spanning tree ([`MinimumSpanningTree.cs`](Smart-X.Api/Network/MinimumSpanningTree.cs))

**The energy model.** A link's weight is the energy to send one bit across it, from the first-order radio model used in wireless sensor network research (Heinzelman et al., see [References](#references)): `cost(d) = 2 × 50 nJ + 0.1 nJ × d²`. The first term is the radio electronics at the transmitter and the receiver; the second grows with the square of the distance. So one long hop costs more than two short ones once a link is longer than about 44.7 m (where 0.1 × d² = 100 nJ), which is why relaying through a nearby device saves power.

**The backbone.** A minimum spanning tree connects every device to the gateway using the cheapest possible set of links, with no loops. Two algorithms are implemented and the mesh runs both:

- **Prim's algorithm**, grown outwards from the gateway with a `PriorityQueue` as the heap of candidate links: O(E log V). It builds the backbone the map shows.
- **Kruskal's algorithm** sorts the links by cost and keeps each one that joins two groups that were not yet connected, using union-find with path compression and union by rank: O(E log E). It is used to *verify* Prim: if their total costs ever disagreed, the API logs a warning and the page's green "Prim and Kruskal agree" badge turns red and says "disagree".

```csharp
// Prim: always take the cheapest link that leaves the tree built so far.
while (frontier.TryDequeue(out var edge, out _))
{
    if (!inTree.Add(edge.B)) continue;   // the far end joined through a cheaper link since this one was queued
    edges.Add(edge);
    total += edge.Weight;
    AddLinksFrom(edge.B);
}
```

**What it saves, and what it does not.** On the 40-device demo estate (see [Testing Part 3](#testing-part-3)) the backbone costs 10,947 nJ/bit against 51,685 for using every link, a saving of 79%. But the tree minimises the *total* cost of the network, not any one device's route: collecting a reading from every device along the backbone costs 17% more than along each device's own cheapest route, and Probe 35 takes 12 hops over the backbone where 5 would do (68% dearer). That is the trade-off of a minimum spanning tree, and the page shows both numbers.

#### Cheapest routes: Dijkstra ([`ShortestPath.cs`](Smart-X.Api/Network/ShortestPath.cs))

Dijkstra's algorithm from the gateway gives every device its cheapest route, O((V + E) log V) with a `PriorityQueue` (which cannot lower an entry's priority, so a cheaper route queues the device again and the dearer entry is skipped when it comes out). The route tracker draws it dashed next to the backbone route, and it is also how the **backup route** is found: remove the device's first hop from the graph and run Dijkstra again.

#### Self-healing

A device is *down* while it has an active Disconnected alert (it has been silent for `Monitor:DisconnectAfterSeconds`). The analyzer removes down devices from the graph and reruns BFS, Prim and Dijkstra. Devices that can no longer reach the gateway are shown as **Isolated**, and comparing the new result with the previous one produces the log: *Down*, *Up*, *CutOff*, *Reconnected* and *Rerouted* (for example "Probe 7 now relays via Probe 22 (was via Probe 2)").

### Scaling evidence

Numbers come from the scripts in the repo root; every figure is from a real run on one laptop. Run them yourself with the commands shown.

**History tree: AVL against alternatives** (`dotnet run benchmark-history.cs`; a window of about 100 readings, 5,000 queries, 56 bytes per reading in memory)

| Readings stored | Tree height (AVL limit) | AVL tree | Sorted array + binary search | Unsorted list scan | AVL vs scan |
|---:|---:|---:|---:|---:|---:|
| 1,000 | 10 (15) | 7.1 µs | 1.1 µs | 6.8 µs | 1× |
| 10,000 | 14 (20) | 7.2 µs | 1.0 µs | 58.0 µs | 8× |
| 100,000 | 17 (25) | 6.7 µs | 1.0 µs | 479.2 µs | 72× |
| 1,000,000 | 20 (29) | 6.1 µs | 2.9 µs | 5,829.5 µs | 957× |

The tree's cost barely moves as the history grows 1,000-fold, while a scan grows in proportion. Honestly: a sorted array is faster for a history that never changes. The tree wins where this system needs it to: readings that arrive out of order (200,000 out-of-order inserts: AVL 250 ms against 2,015 ms for the array), removal for retention, and counts and paging (counting a window that holds half of a million-reading history takes 0.2 µs against 6,486 µs for a scan). Live, a two-hour window out of up to 259,212 readings was answered in 17 to 220 µs on the server, and the whole archive of 440,684 readings reloaded from SQLite in 1.3 s.

**Graph algorithms.** On a 20,000-vertex graph the traversals, Prim, Kruskal and Dijkstra each finish in 81 to 285 ms (`dotnet run test-part3-graph.cs`).

**The mesh service** (`dotnet run test-part3-mesh.cs`)

| Devices | Links | Build the mesh | Analyse (BFS, Prim, Kruskal, Dijkstra) | Fail a device and write the log |
|---:|---:|---:|---:|---:|
| 200 | 828 | 1 ms | 2 ms | 2 ms |
| 1,000 | 4,231 | 14 ms | 16 ms | 15 ms |
| 2,000 | 9,311 | 49 ms | 44 ms | 48 ms |

Building the links compares every pair of devices, so it is O(n²) (fine to a few thousand devices; a grid index would be the upgrade). The layout and links are rebuilt only when a device is added, removed or moved, and the analysis only when the set of down or critical devices changes; requests read the cached result.

**The whole system under load** (`dotnet run loadtest-mesh.cs -- 200 30`, against a scratch database; each device reports every 5 seconds, as the simulator does)

| | 200 devices | 1,000 devices |
|---|---:|---:|
| `GET api/Topology` | 168 KB (25.5 KB compressed), 2 to 6 ms | 816 KB (130 KB compressed), 12 to 26 ms |
| `GET api/Stream/snapshot` | 32 KB (3.3 KB compressed) | 223 KB (31 KB compressed) |
| Ingest with `StandardDelayMs=100` (default) | falls behind: about 10 readings/s handled against 40/s sent, queue grew to 971 | not sustainable: 200 readings/s sent against about 10/s handled (worked out, not run) |
| Ingest with `StandardDelayMs=0` | keeps up; a 5,000-reading burst is absorbed at 850 to 1,375 readings/s | keeps up: the simulator's 200 readings/s were all processed with no failed ticks |

What the load test found and fixed:

- **The simulator stopped above 500 devices.** It posted every device in one batch but the endpoint refuses more than 500 readings. It now posts in chunks of 500.
- **The map froze the page at 1,000 devices** (seven stalls of about 1.5 s in 10 s). Fetching took 45 ms and parsing 1 ms; the time went into redrawing 8,700 SVG elements and into deserialising an 816 KB response on the Blazor WebAssembly interpreter. The map now redraws only when the mesh version, subnet, root or tracked device changes, and `GET api/Topology?since=<version>` returns `204 No Content` when the client already holds the current version. After the fix there were no stalls in 20 seconds, and a device taken offline still appeared within seconds.
- **The SQLite log file grew without limit**, about 13 KB per reading (384 MB after 29,000 readings), because the log was never folded back into the database. The processor now runs `PRAGMA wal_checkpoint(TRUNCATE)` every 1,000 readings; the log stays under about 12 MB and writes stay at over 1,000 a second.
- **Memory and disk grow with retention.** One reading per 5 seconds is 17,280 a day per device: about 1 MB of memory (56 bytes each in the tree) and 2.4 MB of disk (146 bytes a row). With the default 30-day retention, 40 devices need about 1.2 GB of memory and 2.9 GB of disk and 200 devices about 5.8 GB and 14.5 GB, so lower `History:RetentionDays` for large fleets. Holding the whole history in memory is what makes range queries O(log n); a time-series database is the upgrade (see the technology recommendations in the report).

Known limits: selecting a device on a 1,000-device map takes about 0.8 s (a full redraw); the client has no automated tests (it was checked by hand in the browser); and the `since` version restarts when the API restarts.

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

## Testing Part 3

Start the system (see [Running it](#running-it)). First create a demo estate of 40 devices in 5 blocks. In PowerShell, once:

```powershell
$api = "http://localhost:5256/api"
1..40 | ForEach-Object {
    $block = [char](64 + ($_ - 1) % 5 + 1)          # Blocks A to E
    $row = [math]::Ceiling($_ / 10)
    $d = @{ macAddress = "BB:00:00:00:00:{0:X2}" -f $_; location = "Estate/Block $block/Row $row"; category = $_ % 3; description = "Probe $_" }
    Invoke-RestMethod -Method Post -Uri "$api/Sensors" -ContentType "application/json" -Body ($d | ConvertTo-Json) | Out-Null
}
```

Wait about 10 seconds so the simulator has made every device report once, then open **Network Topology and Mesh Routing**. Positions come from each device's location and MAC address, so you will see the same map and numbers below every time.

### 1. The topology dashboard (graph, MST)

**Expect:** a map of 40 devices in five labelled blocks around the gateway (the black square), with the cards above it reading **40 online of 40**, about 126 links, and a **power-efficient backbone** that saves about **79%** against using every link, with a green **Prim and Kruskal agree** badge. The green lines on the map are the backbone; the thin grey lines are the other links; a purple dashed line is a *backhaul* link joining two blocks that are out of radio range of each other.

```powershell
$t = Invoke-RestMethod "$api/Topology"
$t.summary
```

### 2. Track a sensor's routing path by MAC address (graph, Dijkstra)

Type `BB:00:00:00:00:23` into **Track a device by MAC address** (or click that device on the map).

**Expect:** the route tracker lists the route from **Probe 35** to the gateway hop by hop, **12 hops** costing about 3,605 nJ/bit along the backbone (drawn orange on the map), and the cheapest possible route of 5 hops and about 2,142 nJ/bit (drawn dashed blue), so the backbone route costs 68% more for this device, with the explanation that a minimum spanning tree minimises the whole network's cost, not one device's route. It also shows the **backup route** if its first hop failed, and its neighbours.

```powershell
Invoke-RestMethod "$api/Topology/route/BB:00:00:00:00:23" | ConvertTo-Json -Depth 6
```

An unknown MAC returns `404`.

### 3. Who is connected? Trace from a device, and subnets (BFS)

Click **Show who is connected from this device** under the tracked device: every device on the map is numbered with its fewest hops from it. Choose **Estate/Block A** in the subnet list.

**Expect:** the other blocks dim, and a panel for the subnet reads *8 devices in this subnet, 8 reach the gateway, 0 down, 0 cut off*, and that the healthy members form 1 separate network on their own. **Back to the gateway** returns to tracing from the gateway.

```powershell
(Invoke-RestMethod "$api/Topology?subnet=Estate/Block%20A").subnetDetail
```

### 4. Self-healing when a device fails

Take **Probe 2** offline: select it on the Command Stream page and click **Toggle simulated offline**, or

```powershell
Invoke-RestMethod -Method Post "$api/Devices/BB:00:00:00:00:02/simulate-offline"
```

Wait about 17 seconds (the monitor waits 15 s of silence).

**Expect:** the Probe 2 dot turns red with a cross, the cards read **39 online, 1 down**, the **Network Topology** menu item gets a red **1** badge, and the **Self-healing Log** gains *Down: Probe 2 went down* with *Rerouted: Probe 7 now relays via Probe 22 (was via Probe 2)* beneath it. The backbone has been recomputed around the gap. Toggle the device back and it rejoins on its next reading.

### 5. The historical telemetry log (AVL tree, range queries)

Give a device a week of history (about 10,000 readings), then ask for a window of it:

```powershell
Invoke-RestMethod -Method Post "$api/History/BB:00:00:00:00:01/seed?days=7&everySeconds=60"
```

On the Topology page, track `BB:00:00:00:00:01`, scroll to **Historical Telemetry Log**, and pick **Last 24 hours**, then try **Custom range**, the **First / Previous / Next / Last** buttons and the **Newest first** box.

**Expect:** a page of readings ("Showing 1-25 of ...", about 1,440 for the last 24 hours plus whatever the simulator has sent since) with a chart of the page, and a line above the table that says how many readings are in the window and "Looked up in a tree of about 10,000 readings (height 14)" with the lookup time in microseconds. The same query by API:

```powershell
$from = [DateTime]::UtcNow.AddHours(-2).ToString("yyyy-MM-ddTHH:mm:ssZ")
$to = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
Invoke-RestMethod "$api/History/BB:00:00:00:00:01?from=$from&to=$to&skip=0&take=5&newestFirst=false"
```

(The seeded readings stop at the moment you seeded; readings arriving afterwards come from the simulator.)

### 6. Navigation between modules

On the Command Stream page, select a device and click **Show on mesh map**: the Topology page opens with that device tracked. On the Topology page, click **Open in command stream** on the tracked device to go back with it selected. The home page card for Topology describes the module, and the menu badge follows the number of down devices.

### 7. Automated checks and the load test

```bash
dotnet run test-part3-graph.cs     # 48 checks: graph, BFS/DFS, Prim, Kruskal, Dijkstra, a 20,000-vertex scale run
dotnet run test-part3-avl.cs       # 27 checks: tree balance after 30,000 random adds/removes, 10,000 random windows, concurrency, 1M readings
dotnet run test-part3-mesh.cs      # 32 checks: layout, links, failures, the self-healing log, caching, 2,000 devices
dotnet run test-part2.cs           # Part 2's checks
dotnet run benchmark-history.cs    # the AVL tree against a sorted array and a list scan
```

**Expect:** each script ends with `N passing, 0 failing` (or `All checks passed.`). The tests compare the algorithms with independent answers (a `SortedDictionary`, Floyd–Warshall hop counts, a brute-force minimum spanning tree), and were themselves tested by planting bugs and checking that a test fails.

For the load test, run the API on a **scratch database** with the delay off, because it registers real devices:

```powershell
$env:ConnectionStrings__SmartXDb = "Data Source=scratch.db"; $env:Processing__StandardDelayMs = "0"
dotnet run --project Smart-X.Api
```

and in another terminal `dotnet run loadtest-mesh.cs -- 200 30` (devices, seconds). It prints response sizes, queue depth while readings arrive, and how fast a 5,000-reading burst is processed. Delete `scratch.db*` afterwards.

### API summary (Part 3)

| Endpoint | Purpose |
|---|---|
| `GET /api/Topology?subnet=&root=&since=` | The whole mesh: nodes with positions, status and route, every link, the backbone, summary, subnet detail and heal log. `root` (a MAC) traces connections from that device instead of the gateway (`400` if unknown or down); `since` (a version already held) returns `204` when the mesh has not changed |
| `GET /api/Topology/route/{mac}` | One device's backbone route, cheapest route, backup route and neighbours (`404` if unknown) |
| `GET /api/History/{mac}?from=&to=&skip=&take=&newestFirst=` | A page of a device's readings in a time window, with the total and the tree's size, height and lookup time (times in ISO 8601 with a `Z`; `take` up to 1000) |
| `POST /api/History/{mac}/seed?days=&everySeconds=` | Testing hook: backfill a device with believable history (up to 300,000 readings) |

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

- **Predictive suggestions** — [`ActionHistory`](Smart-X.Api/Processing/ActionHistory.cs) keeps the operator's recent alerts, searches and commands in a bounded `Queue<T>` (newest at the back, oldest dropped from the front once it holds 200) and counts how often each action follows another within two minutes (a first-order transition model). [`InsightEngine`](Smart-X.Api/Processing/InsightEngine.cs) looks at the alerts active right now and the operator's last few actions, and suggests the follow-ups that happened in at least two earlier occurrences and at least half of them, ranked by confidence using a `PriorityQueue`. It never re-suggests something already done or a command that would leave a device where it already is.

- **Asynchronous prediction generation** — none of this runs inside a request. A search, command or alert only drops an event, stamped with the time it happened, into a `Channel<T>` ([`ActionFeed`](Smart-X.Api/Processing/ActionFeed.cs)) and returns immediately. [`PredictionService`](Smart-X.Api/Processing/PredictionService.cs), a `BackgroundService`, awaits the channel (or a one-second timer, to keep trend warnings fresh), updates the learning model, recomputes the suggestions on a worker thread with `await Task.Run(...)`, and publishes the result. `GET /api/Insights` and the dashboard snapshot simply read the latest published result, so their cost does not grow with the number of devices or the amount of history.

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

**Part 3**

- Adelson-Velsky, G. M. and Landis, E. M. (1962). "An algorithm for the organization of information", *Doklady Akademii Nauk SSSR* 146(2), 263–266 — the AVL tree and its rotations (`AvlTree`)
- Cormen, T. H., Leiserson, C. E., Rivest, R. L. and Stein, C. *Introduction to Algorithms* (4th ed., MIT Press, 2022) — chapter 17, "Augmenting Data Structures" (chapter 14 in the 3rd edition), on order-statistic trees: the subtree size stored in each `AvlTree` node, and the chapters on graph traversal, minimum spanning trees and single-source shortest paths
- Prim, R. C. (1957). "Shortest connection networks and some generalizations", *Bell System Technical Journal* 36(6), 1389–1401 — `MinimumSpanningTree.Prim`
- Kruskal, J. B. (1956). "On the shortest spanning subtree of a graph and the traveling salesman problem", *Proceedings of the American Mathematical Society* 7(1), 48–50 — `MinimumSpanningTree.Kruskal`
- Dijkstra, E. W. (1959). "A note on two problems in connexion with graphs", *Numerische Mathematik* 1, 269–271 — `ShortestPath.Dijkstra`
- Heinzelman, W. R., Chandrakasan, A. P. and Balakrishnan, H. (2000). ["Energy-efficient communication protocol for wireless microsensor networks"](https://www.cs.montana.edu/courses/csci566/HW/HW2/LEACH.pdf), *Proceedings of the 33rd Hawaii International Conference on System Sciences* — the first-order radio model behind the link costs: 50 nJ/bit for the radio electronics and 100 pJ/bit/m² for the transmit amplifier (the `Mesh` settings; the amplifier figure is written 0.1 nJ/bit/m²)
- [Winter, T. et al. (2012). RFC 6550: RPL, the IPv6 Routing Protocol for Low-Power and Lossy Networks](https://www.rfc-editor.org/rfc/rfc6550) — the routing protocol real low-power meshes use, which builds a tree towards a root much as the backbone here does (see the technology recommendations in the report)
- [PriorityQueue, Queue and Stack](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.priorityqueue-2?view=net-10.0) — the heaps and work lists inside Prim, Dijkstra, BFS and DFS
- [SQLite write-ahead logging](https://www.sqlite.org/wal.html) — checkpointing, and why `TelemetryProcessorService` checkpoints explicitly
- [Response compression in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/response-compression) — `AddResponseCompression` in `Program.cs`
- [ASP.NET Core Blazor WebAssembly build tools and ahead-of-time (AOT) compilation](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot) — why deserialising a large response is slow on the Blazor WebAssembly interpreter

## Project layout

```text
Smart-X.Api/
  Controllers/       Sensors, Telemetry, Stream, Alerts, Devices, Commands, Insights, History, Topology
  Processing/        Pipeline (queues), registry, alert tracker, command history, insight engine, background services
  History/           AvlTree, TelemetryArchive (one tree per device), retention service
  Network/           MeshGraph, GraphTraversal (BFS/DFS), MinimumSpanningTree (Prim/Kruskal), ShortestPath (Dijkstra),
                     mesh layout and builder, topology analyzer and report, topology service
  Data/, Migrations/ EF Core model and migrations
Smart-X.Client/
  Pages/             Home, Ingestion, Manage Sensors, Command Stream, Topology
  Components/        Timeline chart, mesh map, route list, history panel
  Services/          AppState, LiveDataService
  Layout/            Main layout and navigation menu
Smart-X.Shared/      DTOs and domain models used by both projects
test-part2.cs        Self-check script for the Part 2 data structures and engine
test-part3-graph.cs  Graph algorithm checks (BFS, DFS, Prim, Kruskal, Dijkstra)
test-part3-avl.cs    AVL tree and history archive checks
test-part3-mesh.cs   Mesh layout, analyzer, self-healing log and service checks
benchmark-history.cs AVL tree against a sorted array and a list scan
loadtest-mesh.cs     Load test for the mesh and ingest path (run against a scratch database)
FEEDBACK_UPDATES.md  What changed in response to lecturer feedback
```
