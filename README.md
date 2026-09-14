# Smart-X

A vineyard IoT telemetry system. Sensors (soil moisture, canopy temperature, power draw, actuators like irrigation valves) report readings to a central API, which stores history and simulates live drift for demo purposes. A Blazor web app lets you register sensors, ingest/view telemetry, and manage the sensor fleet.

**Demo video:** https://youtu.be/EgvMt7jgL68

## Stack

- **Smart-X.Api** — ASP.NET Core Web API + EF Core + SQLite
- **Smart-X.Client** — Blazor WebAssembly
- **Smart-X.Shared** — DTOs and models shared between the two

## Running it

### Option 1: Docker (recommended)

```bash
docker compose up -d
```

Then open **http://localhost:5128**. The API is on **http://localhost:5256**. Data (SQLite DB + uploaded files) persists in `docker-data/` across restarts.

```bash
docker compose down
```

On Windows, double-clicking [`docker-setup.bat`](docker-setup.bat) does the same thing without touching a terminal — it builds and starts the stack, waits for the client to actually respond, then opens it in your browser.

### Option 2: Local dev

Run the API and client in separate terminals from the repo root:

```bash
dotnet run --project Smart-X.Api
```

```bash
dotnet run --project Smart-X.Client
```

The database is auto-created and migrated on first run — no manual `dotnet ef` steps needed either way.

## Features

- Register and manage sensors (soil moisture, canopy temp, power, actuators), each with a MAC address, location, category and description
- Deactivate / reactivate / hard-delete sensors
- Ingest telemetry readings, with the value type locked to the sensor's category to prevent mismatched data
- Live sensor status dashboard with freshness indicators, auto-refreshing every 5 seconds
- A background service continuously simulates realistic drift for every active sensor, so the dashboard updates on its own
- Attach and download files against a sensor (e.g. install photos, logs)

## Technical requirements

A few language/CLR features the brief calls for, and where they live:

- **Generics** — [`TelemetryPacket<T>`](Smart-X.Shared/TelemetryPacket.cs) wraps a single reading with `T : struct`, so `float`, `int`, and `bool` payloads are stored and read as their real value type instead of boxing everything to `object`. `TelemetryController` picks the concrete `T` based on the sensor's category before handing the packet off to be normalized into a `SensorReading`.

- **Operator overloading** — [`SensorReading`](Smart-X.Shared/SensorReading.cs) overloads `+`, `-`, `<`, and `>` so readings can be compared and combined directly instead of reaching into `.Value` everywhere. `TelemetryHistoryStore.GetTrend` (`GET /api/Telemetry/{deviceId}/trend`) uses `-`, `<`, and `>` to compute the delta and direction between a sensor's two most recent readings; `GetCategoryTotal` (`GET /api/Telemetry/total/{category}`) uses `+` to sum the latest reading across every sensor in a category. Both surface directly in the Ingestion page's Sensor Status Overview — a Rising/Falling/Stable badge per sensor and a category totals line. Mismatched categories or device IDs throw rather than silently producing a meaningless result.

- **Recursion** — [`DeploymentNode.ValidateNode`](Smart-X.Shared/DeploymentNode.cs) recursively walks a facility → zone → subzone → device hierarchy, failing the whole subtree if any child node has a blank name. `SensorsController` builds this tree from a sensor's `Location` field (a `/`-delimited path like `Estate/Block A/Row 3`, via `DeploymentNode.FromPath`) and rejects registration or updates that don't validate — so a location like `Estate//Row 3` is caught server-side instead of silently stored.

- **Custom data structure** — [`SensorHistoryBuffer`](Smart-X.Api/SensorHistoryBuffer.cs) is a fixed-capacity circular buffer: `Add` is O(1) and once full, the oldest reading is overwritten rather than the array growing forever. Each sensor gets one of these in `TelemetryHistoryStore`, which is what keeps the live dashboard fast no matter how long a sensor has been reporting.

- **Background processing** — [`TelemetrySimulatorService`](Smart-X.Api/TelemetrySimulatorService.cs) is an ASP.NET Core `BackgroundService` that ticks every 5 seconds, drifting every active sensor's value by a small random delta (clamped per category, with occasional toggles for actuators) and writing it to both the database and the in-memory buffer. This is what makes the dashboard update on its own without any user action.

- **Jagged arrays** — [`MockTelemetryGenerator.GenerateBatches`](Smart-X.Api/MockTelemetryGenerator.cs) returns a `float[][]` where each row (device) has a different number of readings. `SensorsController.RegisterSensor` calls it to seed a newly registered Environmental sensor with a handful of plausible historical readings, so its history/dashboard isn't empty before the background simulator has ticked.

## References

- [How do I convert int/decimal to float in C#?](https://stackoverflow.com/questions/1042099/how-do-i-convert-int-decimal-to-float-in-c) — normalizing `TelemetryPacket<T>` values to `float` in `TelemetryController`
- [Most efficient way to save a byte array as a file on disk in C#](https://stackoverflow.com/questions/128674/what-is-the-most-efficient-way-to-save-a-byte-array-as-a-file-on-disk-in-c) — streaming uploaded files to disk in `SensorsController.UploadSensorFile`
- [Background tasks with hosted services in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services) — `BackgroundService` base class used by `TelemetrySimulatorService`
- [Operator overloading — C# reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/operator-overloading) — the `+`, `-`, `<`, `>` overloads on `SensorReading`
- [Applying migrations — EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) — `Database.Migrate()` auto-applying migrations on startup in `Program.cs`
- [ASP.NET Core Blazor file uploads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-uploads) — `InputFile`/`IBrowserFile` usage in the Attach Files section of `Ingestion.razor`

## Project layout

```
Smart-X.Api/        Controllers, EF Core DbContext & migrations, telemetry simulator
Smart-X.Client/      Blazor pages (Ingestion, Manage Sensors, Home)
Smart-X.Shared/      DTOs and domain models used by both projects
```

## References

- [How do I convert int/decimal to float in C#?](https://stackoverflow.com/questions/1042099/how-do-i-convert-int-decimal-to-float-in-c) - normalizing `TelemetryPacket<T>` values to `float` in `TelemetryController`
- [Most efficient way to save a byte array as a file on disk in C#](https://stackoverflow.com/questions/128674/what-is-the-most-efficient-way-to-save-a-byte-array-as-a-file-on-disk-in-c) - streaming uploaded files to disk in `SensorsController.UploadSensorFile`
- [Background tasks with hosted services in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services) - `BackgroundService` base class used by `TelemetrySimulatorService`
- [Operator overloading — C# reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/operator-overloading) - the `+`, `-`, `<`, `>` overloads on `SensorReading`
- [Applying migrations — EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) - `Database.Migrate()` auto-applying migrations on startup in `Program.cs`
- [ASP.NET Core Blazor file uploads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-uploads) - `InputFile`/`IBrowserFile` usage in the Attach Files section of `Ingestion.razor`
