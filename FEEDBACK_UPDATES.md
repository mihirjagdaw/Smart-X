# Updates made from lecturer feedback

Part 1 received no feedback. Part 2 received feedback on one item, and this file lists what was changed because of it.

## Feedback received

| # | Part | Item | Mark | Feedback |
|---|---|---|---|---|
| 1 | 2 | Predictive Action and Suggestion Engine | 20/30 | The rubric's expected `Queue<T>` history and asynchronous processing (`Task`/`async` for prediction generation) are not implemented in the engine path; the logic is synchronous in the request flow. |

## Update 1: `Queue<T>` history and asynchronous prediction generation

**1. History held in a `Queue<T>`.** [`ActionHistory`](Smart-X.Api/Processing/ActionHistory.cs) kept the operator's recent alerts, searches and commands in a `List<T>` that was pruned by scanning it. It now keeps them in a bounded `Queue<RecentEvent>`: a new event is enqueued at the back and, once the queue holds 200, the oldest is dequeued from the front, so keeping the history bounded costs O(1) instead of a scan.

**2. Prediction generation moved out of the request path.** Before, a search, command or alert updated the learning model and recomputed the suggestions inside the HTTP request that caused it. Now:

- A request only records the action: it writes a timestamped event into a `Channel<T>` ([`ActionFeed`](Smart-X.Api/Processing/ActionFeed.cs)) and returns at once.
- [`PredictionService`](Smart-X.Api/Processing/PredictionService.cs), a `BackgroundService`, awaits that channel, updates `ActionHistory`, and builds the new suggestions with `await Task.Run(...)` on a worker thread. It publishes the finished result.
- `GET /api/Insights` and `GET /api/Stream/snapshot` only read the latest published result.

The dependency-injection cycle this created (the alert tracker needed the prediction service, which needs the insight engine, which needs the tracker) was removed by splitting "record an action" (the channel behind the one-method interface `IActionRecorder`) from "learn from actions" (the background service).

## How it was checked

- `dotnet run test-part2.cs` gained 14 checks for the new behaviour: the history stays at 200 and drops the oldest, the two-minute window still applies, a suggestion appears in the background, a 5,000-action burst is absorbed in under half a second, and event timestamps are preserved. All earlier Part 2 checks still pass (50 in total).
- Live run: a suggestion appeared 10 to 47 ms after the reading that caused it, and `GET /api/Insights` averaged 0.8 ms.
- The README's Part 2 section describes the new design.
