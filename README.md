# Smart-X

A vineyard IoT telemetry system. Sensors (soil moisture, canopy temperature, power draw, actuators like irrigation valves) report readings to a central API, which stores history and simulates live drift for demo purposes. A Blazor web app lets you register sensors, ingest/view telemetry, and manage the sensors.

## Stack

- **Smart-X.Api** - ASP.NET Core Web API + EF Core + SQLite
- **Smart-X.Client** - Blazor WebAssembly
- **Smart-X.Shared** - DTOs and models shared between the two

## Running it

### Option 1: Docker (recommended)

```bash
docker compose up -d
```

Then open **http://localhost:5128**. The API is on **http://localhost:5256**. Data (SQLite DB + uploaded files) persists in `docker-data/` across restarts.

```bash
docker compose down
```

### Option 2: Local dev

Run the API and client in separate terminals from the repo root:

```bash
dotnet run --project Smart-X.Api
```

```bash
dotnet run --project Smart-X.Client
```

The database is auto-created and migrated on first run, so no manual `dotnet ef` steps needed either way.

## Features

- Register and manage sensors (soil moisture, canopy temp, power, actuators), each with a MAC address, location, category and description
- Deactivate / reactivate / hard-delete sensors
- Ingest telemetry readings, with the value type locked to the sensor's category to prevent mismatched data
- Live sensor status dashboard with freshness indicators, auto-refreshing every 5 seconds
- A background service continuously simulates realistic drift for every active sensor, so the dashboard updates on its own
- Attach and download files against a sensor (e.g. install photos, logs)
  
## Technical requirements

The features required by the POE and where they live:

- **Generics** — [`TelemetryPacket<T>`](Smart-X.Shared/TelemetryPacket.cs) wraps a single reading with `T : struct`, so that the `float`, `int`, and `bool` payloads are stored and read as their actual value type instead of boxing everything to `object`. `TelemetryController` then picks the concrete `T` based on the sensor's category before handing the packet off to be normalized into a `SensorReading`.

- **Operator overloading** — [`SensorReading`](Smart-X.Shared/SensorReading.cs) overloads `+`, `-`, `<`, and `>` so readings from the same sensor/category can be compared and combined directly (e.g. `readingB - readingA` for a delta over time) instead of reaching into `.Value` everywhere. Mismatched categories or device IDs throw instead of silently producing a meaningless result.

- **Recursion** — [`DeploymentNode.ValidateNode`](Smart-X.Shared/DeploymentNode.cs) walks a facility → zone → subzone → device hierarchy, recursively validating every child node and failing the whole subtree if one child is invalid. Models how a real vineyard's sensors would be organised by physical location, not just a flat list.

- **Custom data structure** — [`SensorHistoryBuffer`](Smart-X.Api/SensorHistoryBuffer.cs) is a fixed-capacity circular buffer: `Add` is O(1) and once full, the oldest reading is overwritten rather than the array growing forever. Each sensor gets one of these in `TelemetryHistoryStore`, which is what keeps the live dashboard fast no matter how long a sensor has been reporting.

- **Background processing** — [`TelemetrySimulatorService`](Smart-X.Api/TelemetrySimulatorService.cs) is an ASP.NET Core `BackgroundService` that ticks every 5 seconds, drifting every active sensor's value by a small random delta (clamped per category, with occasional toggles for actuators) and writing it to both the database and the in-memory buffer. This is what makes the dashboard update on its own without any user action.

- **Jagged arrays** — [`MockTelemetryGenerator.GenerateBatches`](Smart-X.Api/MockTelemetryGenerator.cs) returns a `float[][]` where each row (device) has a different number of readings, used for an early bulk-seeding demo of realistic canopy-temperature data.
## Project layout

```
Smart-X.Api/        Controllers, EF Core DbContext & migrations, telemetry simulator
Smart-X.Client/      Blazor pages (Ingestion, Manage Sensors, Home)
Smart-X.Shared/      DTOs and domain models used by both projects
```

