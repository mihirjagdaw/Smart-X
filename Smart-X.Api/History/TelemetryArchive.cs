using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Smart_X.Api.Data;

namespace Smart_X.Api.History
{
    public record ArchivedReading(DateTimeOffset Timestamp, float Value);

    public record ArchivePage(int Total, List<ArchivedReading> Items, int TreeSize, int TreeHeight, double ElapsedMicroseconds);

    /// <summary>
    /// The long-term telemetry history: one AvlTree per device, keyed by timestamp. The recent-readings buffers
    /// from Part 2 only hold the last few minutes; this keeps days of readings in an order that can be searched
    /// by time range without reading the ones outside it. Safe to use from several threads.
    /// </summary>
    public class TelemetryArchive
    {
        private readonly Dictionary<string, AvlTree<long, float>> _trees = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public int ReadingCount
        {
            get
            {
                lock (_lock)
                {
                    return _trees.Values.Sum(t => t.Count);
                }
            }
        }

        public int DeviceCount
        {
            get
            {
                lock (_lock)
                {
                    return _trees.Count;
                }
            }
        }

        public void Add(string mac, DateTimeOffset timestamp, float value)
        {
            lock (_lock)
            {
                if (!_trees.TryGetValue(mac, out var tree))
                {
                    tree = new AvlTree<long, float>();
                    _trees[mac] = tree;
                }

                // shortcut: two readings in the same 100 ns tick are kept by moving the second one tick later,
                // which is far below anything a chart or query can resolve; use a (tick, sequence) key if exact times matter.
                var key = timestamp.UtcTicks;
                while (!tree.Add(key, value)) key++;
            }
        }

        public void Remove(string mac)
        {
            lock (_lock)
            {
                _trees.Remove(mac);
            }
        }

        /// <summary>
        /// The readings from "from" to "to" (both included), one page at a time. Newest first is how a log reads;
        /// oldest first is how a chart reads. The total is counted without visiting the readings in the window.
        /// </summary>
        public ArchivePage Query(string mac, DateTimeOffset from, DateTimeOffset to, int skip, int take, bool newestFirst)
        {
            var started = Stopwatch.GetTimestamp();

            lock (_lock)
            {
                if (!_trees.TryGetValue(mac, out var tree) || take <= 0)
                {
                    return new ArchivePage(0, new List<ArchivedReading>(), 0, 0, Elapsed(started));
                }

                long low = from.UtcTicks, high = to.UtcTicks;
                var total = tree.CountBetween(low, high);

                // Positions inside the window to read. A newest-first page counts back from the end of the window.
                var end = newestFirst ? total - skip : Math.Min(total, skip + take);
                var begin = newestFirst ? Math.Max(0, end - take) : skip;
                var count = Math.Max(0, end - begin);

                var items = count == 0
                    ? new List<ArchivedReading>()
                    : tree.Slice(tree.CountLess(low) + begin, count).Select(e => new ArchivedReading(new DateTimeOffset(e.Key, TimeSpan.Zero), e.Value)).ToList();
                if (newestFirst) items.Reverse();

                return new ArchivePage(total, items, tree.Count, tree.Height, Elapsed(started));
            }
        }

        /// <summary>Drops every reading older than the cutoff from memory. Returns how many were dropped.</summary>
        public int TrimBefore(DateTimeOffset cutoff)
        {
            var removed = 0;
            lock (_lock)
            {
                foreach (var tree in _trees.Values)
                {
                    while (tree.TryGetMin(out var oldest, out _) && oldest < cutoff.UtcTicks)
                    {
                        tree.Remove(oldest);
                        removed++;
                    }
                }
            }
            return removed;
        }

        /// <summary>Fills the archive from the database at startup, after deleting readings past the retention window.</summary>
        public async Task<int> LoadAsync(AppDbContext db, DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            await DeleteOlderThanAsync(db, cutoff, cancellationToken);

            var loaded = 0;
            var rows = db.TelemetryRecords.AsNoTracking().Select(r => new { r.DeviceId, r.Timestamp, r.Value }).AsAsyncEnumerable();
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                Add(row.DeviceId, row.Timestamp, row.Value);
                loaded++;
            }
            return loaded;
        }

        /// <summary>Applies the retention window to the database and to memory.</summary>
        public async Task<int> TrimAsync(AppDbContext db, DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            var deleted = await DeleteOlderThanAsync(db, cutoff, cancellationToken);
            TrimBefore(cutoff);
            return deleted;
        }

        // Plain SQL on purpose: EF Core's SQLite provider cannot translate a DateTimeOffset comparison. SQLite stores
        // these as text ("2026-11-11 15:40:34.3736261+00:00"), and every timestamp here is UTC (+00:00), so comparing
        // the text compares the times.
        private static Task<int> DeleteOlderThanAsync(AppDbContext db, DateTimeOffset cutoff, CancellationToken cancellationToken) =>
            db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"TelemetryRecords\" WHERE \"Timestamp\" < {cutoff.ToUniversalTime()}", cancellationToken);

        private static double Elapsed(long started) => (Stopwatch.GetTimestamp() - started) * 1_000_000.0 / Stopwatch.Frequency;
    }
}
