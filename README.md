# Smart-X

A vineyard IoT telemetry system. Sensors (soil moisture, canopy temperature, power draw, actuators like irrigation valves) report readings to a central API, which stores history and simulates live drift for demo purposes. A Blazor web app lets you register sensors, ingest/view telemetry, and manage the sensor fleet.

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

## Project layout

```
Smart-X.Api/        Controllers, EF Core DbContext & migrations, telemetry simulator
Smart-X.Client/      Blazor pages (Ingestion, Manage Sensors, Home)
Smart-X.Shared/      DTOs and domain models used by both projects
```

