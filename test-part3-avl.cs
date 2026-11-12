#:project Smart-X.Api/Smart-X.Api.csproj
using System.Diagnostics;
using Smart_X.Api.History;

// Checks the AVL tree and the telemetry archive built on it.
//   dotnet run test-part3-avl.cs                 everything
//   dotnet run test-part3-avl.cs -- avl          one section: avl, queries, archive, perf
// Every query is compared with an independent answer from a plain sorted list.

var sections = new HashSet<string>(args.Select(a => a.ToLowerInvariant()));
bool Wants(string name) => sections.Count == 0 || sections.Contains(name);
var rng = new Random(20261113);
int passed = 0, failed = 0;

void Section(string title) => Console.WriteLine($"\n== {title} ==");

void Test(string name, Action body)
{
    try
    {
        body();
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}\n          {ex.GetType().Name}: {ex.Message}");
    }
}

void Expect(bool ok, string message = "expectation failed")
{
    if (!ok) throw new Exception(message);
}

// The tallest an AVL tree of n nodes can be: about 1.44 * log2(n + 2).
int HeightLimit(int n) => (int)Math.Ceiling(1.45 * Math.Log2(n + 2));

List<KeyValuePair<long, int>> Dump(AvlTree<long, int> tree) => tree.Slice(0, tree.Count).ToList();

AvlTree<long, int> RandomTree(int n, long keyRange, out List<long> sortedKeys)
{
    var tree = new AvlTree<long, int>();
    var keys = new HashSet<long>();
    while (keys.Count < n)
    {
        var key = rng.NextInt64(keyRange);
        if (keys.Add(key)) tree.Add(key, (int)(key % 1000));
    }
    sortedKeys = keys.OrderBy(k => k).ToList();
    return tree;
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("avl"))
{
    Section("AvlTree: structure");

    Test("a new tree is empty", () =>
    {
        var t = new AvlTree<long, int>();
        Expect(t.Count == 0 && t.Height == 0, "empty means count 0 and height 0");
        Expect(!t.ContainsKey(1) && !t.TryGetMin(out _, out _));
        Expect(!t.Between(0, 100).Any() && !t.Slice(0, 5).Any());
        Expect(t.CountLess(5) == 0 && t.CountBetween(0, 100) == 0);
    });

    Test("Add says whether the key was new and never replaces an existing value", () =>
    {
        var t = new AvlTree<long, int>();
        Expect(t.Add(10, 1), "first add is new");
        Expect(!t.Add(10, 2), "second add of the same key is not");
        Expect(t.Count == 1 && t.TryGetValue(10, out var v) && v == 1, "the original value stays");
    });

    Test("the four rotation cases each keep a three-node tree two levels tall", () =>
    {
        foreach (var order in new[] { new long[] { 3, 2, 1 }, new long[] { 1, 2, 3 }, new long[] { 3, 1, 2 }, new long[] { 1, 3, 2 } })
        {
            var t = new AvlTree<long, int>();
            foreach (var k in order) t.Add(k, 0);
            Expect(t.Height == 2, $"inserting {string.Join(",", order)} left the tree {t.Height} levels tall (a missing rotation makes it 3)");
            t.AssertInvariants();
            Expect(Dump(t).Select(e => e.Key).SequenceEqual(new long[] { 1, 2, 3 }));
        }
    });

    Test("5,000 random inserts come out sorted and satisfy every AVL rule", () =>
    {
        var t = RandomTree(5000, 1_000_000, out var keys);
        t.AssertInvariants();
        Expect(Dump(t).Select(e => e.Key).SequenceEqual(keys), "in-order contents differ from the sorted keys");
        Expect(t.Height <= HeightLimit(t.Count), $"height {t.Height} is over the AVL limit {HeightLimit(t.Count)}");
    });

    Test("100,000 increasing keys (a timestamp stream) stay balanced instead of becoming a list", () =>
    {
        var t = new AvlTree<long, int>();
        for (long k = 0; k < 100_000; k++) t.Add(k, 0);
        t.AssertInvariants();
        Expect(t.Height <= HeightLimit(100_000), $"height {t.Height} for 100,000 keys; an unbalanced tree would be 100,000 tall");
    });

    Test("100,000 decreasing keys stay balanced too", () =>
    {
        var t = new AvlTree<long, int>();
        for (long k = 100_000; k > 0; k--) t.Add(k, 0);
        t.AssertInvariants();
        Expect(t.Height <= HeightLimit(100_000), $"height {t.Height}");
    });

    Test("Remove handles a missing key, leaves, one-child nodes, two-child nodes and the root", () =>
    {
        var t = new AvlTree<long, int>();
        foreach (var k in new long[] { 50, 30, 70, 20, 40, 60, 80, 10, 45, 65 }) t.Add(k, 0);

        Expect(!t.Remove(999), "removing a key that is not there returns false");
        foreach (var k in new long[] { 10, 20, 70, 50, 45, 30 })
        {
            Expect(t.Remove(k), $"removing {k}");
            t.AssertInvariants();
            Expect(!t.ContainsKey(k));
        }
        Expect(Dump(t).Select(e => e.Key).SequenceEqual(new long[] { 40, 60, 65, 80 }), "the survivors are wrong");

        foreach (var k in new long[] { 40, 60, 65, 80 }) t.Remove(k);
        Expect(t.Count == 0 && t.Height == 0, "removing everything leaves an empty tree");
    });

    Test("30,000 random adds and removes agree with a SortedDictionary and keep every AVL rule", () =>
    {
        var t = new AvlTree<long, int>();
        var oracle = new SortedDictionary<long, int>();

        for (int step = 0; step < 30_000; step++)
        {
            var key = rng.NextInt64(2000);
            if (rng.Next(3) < 2)
            {
                var value = rng.Next();
                Expect(t.Add(key, value) == oracle.TryAdd(key, value), $"step {step}: Add({key})");
            }
            else
            {
                Expect(t.Remove(key) == oracle.Remove(key), $"step {step}: Remove({key})");
            }

            Expect(t.Count == oracle.Count, $"step {step}: count {t.Count} vs {oracle.Count}");
            var probe = rng.NextInt64(2000);
            Expect(t.TryGetValue(probe, out var got) == oracle.TryGetValue(probe, out var expected) && (!oracle.ContainsKey(probe) || got == expected), $"step {step}: lookup {probe}");

            if (step % 500 == 0)
            {
                t.AssertInvariants();
                Expect(Dump(t).Select(e => (e.Key, e.Value)).SequenceEqual(oracle.Select(e => (e.Key, e.Value))), $"step {step}: contents differ");
            }
        }

        t.AssertInvariants();
        Expect(t.Height <= HeightLimit(Math.Max(t.Count, 1)), "final height over the limit");
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("queries"))
{
    Section("AvlTree: range queries and paging");

    var tree = RandomTree(5000, 100_000, out var keys);
    var sorted = keys.Select(k => new KeyValuePair<long, int>(k, (int)(k % 1000))).ToList();

    (long From, long To) RandomWindow()
    {
        // Mix of ordinary windows, reversed bounds, single points, exact key hits and windows past either end.
        return rng.Next(6) switch
        {
            0 => (rng.NextInt64(100_000), rng.NextInt64(100_000)),
            1 => (keys[rng.Next(keys.Count)], keys[rng.Next(keys.Count)]),
            2 => (keys[0], keys[^1]),
            3 => (-500, 150_000),
            4 => (keys[10], keys[10]),
            _ => (200_000, 300_000)
        };
    }

    Test("CountLess and CountLessOrEqual match the sorted list", () =>
    {
        for (int i = 0; i < 5000; i++)
        {
            var probe = i % 3 == 0 ? keys[rng.Next(keys.Count)] : rng.NextInt64(-10, 100_010);
            Expect(tree.CountLess(probe) == sorted.Count(e => e.Key < probe), $"CountLess({probe})");
            Expect(tree.CountLessOrEqual(probe) == sorted.Count(e => e.Key <= probe), $"CountLessOrEqual({probe})");
        }
    });

    Test("10,000 random windows return exactly the entries a plain filter finds, in order", () =>
    {
        for (int i = 0; i < 10_000; i++)
        {
            var (from, to) = RandomWindow();
            var expected = sorted.Where(e => e.Key >= from && e.Key <= to).ToList();
            var actual = tree.Between(from, to).ToList();
            Expect(actual.SequenceEqual(expected), $"window [{from}, {to}]: expected {expected.Count} entries, got {actual.Count}");
            Expect(tree.CountBetween(from, to) == expected.Count, $"CountBetween [{from}, {to}] expected {expected.Count}");
        }
    });

    Test("both ends of a window are included", () =>
    {
        var t = new AvlTree<long, int>();
        foreach (var k in new long[] { 10, 20, 30, 40, 50 }) t.Add(k, 0);
        Expect(t.Between(20, 40).Select(e => e.Key).SequenceEqual(new long[] { 20, 30, 40 }));
        Expect(t.Between(21, 39).Select(e => e.Key).SequenceEqual(new long[] { 30 }));
        Expect(!t.Between(41, 49).Any() && !t.Between(40, 20).Any());
    });

    Test("2,000 random pages match Skip/Take on a plain filter", () =>
    {
        for (int i = 0; i < 2000; i++)
        {
            var (from, to) = RandomWindow();
            int skip = rng.Next(0, 4) == 0 ? rng.Next(0, 6000) : rng.Next(0, 50);
            int take = rng.Next(0, 5) == 0 ? rng.Next(0, 6000) : rng.Next(1, 60);
            var expected = sorted.Where(e => e.Key >= from && e.Key <= to).Skip(skip).Take(take).ToList();
            var actual = tree.Between(from, to, skip, take).ToList();
            Expect(actual.SequenceEqual(expected), $"window [{from}, {to}] skip {skip} take {take}: expected {expected.Count}, got {actual.Count}");
        }
    });

    Test("ElementAt returns the entry at each position and rejects positions that do not exist", () =>
    {
        for (int rank = 0; rank < sorted.Count; rank += 7) Expect(tree.ElementAt(rank).Equals(sorted[rank]), $"rank {rank}");
        Expect(tree.ElementAt(sorted.Count - 1).Equals(sorted[^1]));
        try { tree.ElementAt(sorted.Count); throw new Exception("expected an exception for a rank past the end"); }
        catch (ArgumentOutOfRangeException) { }
        try { tree.ElementAt(-1); throw new Exception("expected an exception for a negative rank"); }
        catch (ArgumentOutOfRangeException) { }
    });

    Test("Slice is lazy: taking one entry from a huge range does not walk the range", () =>
    {
        var big = new AvlTree<long, int>();
        for (long k = 0; k < 200_000; k++) big.Add(k, 0);
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 2000; i++) _ = big.Between(0, 199_999).First();
        Expect(clock.ElapsedMilliseconds < 500, $"2,000 'first of 200,000' lookups took {clock.ElapsedMilliseconds} ms; a full walk would take far longer");
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("archive"))
{
    Section("TelemetryArchive");

    var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    // 250 readings, one a second, value = its position, so a page is easy to recognise.
    TelemetryArchive Filled()
    {
        var a = new TelemetryArchive();
        for (int i = 0; i < 250; i++) a.Add("AA:01", start.AddSeconds(i), i);
        return a;
    }

    Test("a window returns its readings oldest first, and counts them all", () =>
    {
        var page = Filled().Query("AA:01", start.AddSeconds(10), start.AddSeconds(19), 0, 100, newestFirst: false);
        Expect(page.Total == 10 && page.Items.Count == 10, $"expected 10, got total {page.Total} and {page.Items.Count} items");
        Expect(page.Items.Select(r => (int)r.Value).SequenceEqual(Enumerable.Range(10, 10)), "wrong readings or order");
        Expect(page.Items[0].Timestamp == start.AddSeconds(10), "timestamps should come back as stored");
    });

    Test("newest-first pages walk back from the end of the window", () =>
    {
        var a = Filled();
        var first = a.Query("AA:01", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 0, 100, newestFirst: true);
        Expect(first.Total == 250, "total is every reading in the window");
        Expect(first.Items.Select(r => (int)r.Value).SequenceEqual(Enumerable.Range(150, 100).Reverse()), "page 1 should be readings 249 down to 150");

        var second = a.Query("AA:01", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 100, 100, newestFirst: true);
        Expect(second.Items.Select(r => (int)r.Value).SequenceEqual(Enumerable.Range(50, 100).Reverse()), "page 2 should be 149 down to 50");

        var last = a.Query("AA:01", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 200, 100, newestFirst: true);
        Expect(last.Items.Select(r => (int)r.Value).SequenceEqual(Enumerable.Range(0, 50).Reverse()), "the last page is short: 49 down to 0");

        var past = a.Query("AA:01", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 300, 100, newestFirst: true);
        Expect(past.Items.Count == 0 && past.Total == 250, "a page past the end is empty but the total is still reported");
    });

    Test("oldest-first pages and newest-first pages cover the same readings", () =>
    {
        var a = Filled();
        var up = new List<int>();
        for (int skip = 0; skip < 250; skip += 70)
            up.AddRange(a.Query("AA:01", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, skip, 70, newestFirst: false).Items.Select(r => (int)r.Value));
        var down = new List<int>();
        for (int skip = 0; skip < 250; skip += 70)
            down.AddRange(a.Query("AA:01", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, skip, 70, newestFirst: true).Items.Select(r => (int)r.Value));
        Expect(up.SequenceEqual(Enumerable.Range(0, 250)), "oldest-first pages should list 0..249");
        Expect(down.SequenceEqual(Enumerable.Range(0, 250).Reverse()), "newest-first pages should list 249..0");
    });

    Test("2,000 random queries match a plain filter in both directions", () =>
    {
        var a = Filled();
        var all = Enumerable.Range(0, 250).Select(i => (At: start.AddSeconds(i), Value: (float)i)).ToList();
        for (int i = 0; i < 2000; i++)
        {
            var from = start.AddSeconds(rng.Next(-20, 270));
            var to = start.AddSeconds(rng.Next(-20, 270));
            int skip = rng.Next(0, 300), take = rng.Next(1, 120);
            bool newest = rng.Next(2) == 0;

            var inWindow = all.Where(r => r.At >= from && r.At <= to).ToList();
            var expected = (newest ? Enumerable.Reverse(inWindow) : inWindow).Skip(skip).Take(take).Select(r => r.Value).ToList();
            var actual = a.Query("AA:01", from, to, skip, take, newest);

            Expect(actual.Total == inWindow.Count, $"[{from:T}..{to:T}] total {actual.Total}, expected {inWindow.Count}");
            Expect(actual.Items.Select(r => r.Value).SequenceEqual(expected), $"[{from:T}..{to:T}] skip {skip} take {take} newest {newest}: wrong page");
        }
    });

    Test("two readings with the same timestamp are both kept", () =>
    {
        var a = new TelemetryArchive();
        for (int i = 0; i < 3; i++) a.Add("AA:01", start, i);
        var page = a.Query("AA:01", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 0, 10, newestFirst: false);
        Expect(page.Total == 3 && page.Items.Select(r => (int)r.Value).SequenceEqual(new[] { 0, 1, 2 }), "all three should be kept, in arrival order");
    });

    Test("an unknown device has an empty history, and MAC lookups ignore case", () =>
    {
        var a = Filled();
        Expect(a.Query("NOPE", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 0, 10, true).Total == 0);
        Expect(a.Query("aa:01", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 0, 10, true).Total == 250);
    });

    Test("removing a device clears its history only", () =>
    {
        var a = Filled();
        a.Add("AA:02", start, 1);
        a.Remove("aa:01");
        Expect(a.Query("AA:01", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 0, 10, true).Total == 0, "history should be gone");
        Expect(a.Query("AA:02", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 0, 10, true).Total == 1, "other devices must be untouched");
        Expect(a.DeviceCount == 1 && a.ReadingCount == 1);
    });

    Test("TrimBefore drops only the older readings and the rest still query correctly", () =>
    {
        var a = Filled();
        var removed = a.TrimBefore(start.AddSeconds(100));
        Expect(removed == 100, $"expected 100 dropped, got {removed}");
        var page = a.Query("AA:01", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 0, 1000, newestFirst: false);
        Expect(page.Total == 150 && (int)page.Items[0].Value == 100 && (int)page.Items[^1].Value == 249, "readings 100..249 should remain");
        Expect(a.TrimBefore(start.AddSeconds(100)) == 0, "trimming again removes nothing");
    });

    Test("a query reports how big and how tall the tree was, and how long it took", () =>
    {
        var a = new TelemetryArchive();
        for (int i = 0; i < 100_000; i++) a.Add("AA:01", start.AddSeconds(i), i);
        var page = a.Query("AA:01", start.AddSeconds(500), start.AddSeconds(900), 0, 50, true);
        Expect(page.TreeSize == 100_000 && page.TreeHeight <= HeightLimit(100_000), $"size {page.TreeSize}, height {page.TreeHeight}");
        Expect(page.ElapsedMicroseconds >= 0 && page.ElapsedMicroseconds < 50_000, $"{page.ElapsedMicroseconds} microseconds");
    });

    Test("four writers and four readers at once lose nothing and never throw", () =>
    {
        var a = new TelemetryArchive();
        var errors = new List<Exception>();
        using var stop = new CancellationTokenSource();

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var page = a.Query("AA:01", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 0, 50, newestFirst: true);
                    if (page.Items.Count > 1 && page.Items[0].Timestamp < page.Items[1].Timestamp) throw new Exception("page out of order");
                }
                catch (Exception ex) { lock (errors) errors.Add(ex); }
            }
        })).ToArray();

        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (int i = 0; i < 20_000; i++) a.Add("AA:01", start.AddMilliseconds(i * 4 + w), i);
        })).ToArray();

        Task.WaitAll(writers);
        stop.Cancel();
        Task.WaitAll(readers);

        Expect(errors.Count == 0, errors.Count == 0 ? "" : errors[0].Message);
        Expect(a.ReadingCount == 80_000, $"expected 80,000 readings, got {a.ReadingCount}");
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("perf"))
{
    Section("AvlTree: scale");

    const int bigCount = 1_000_000;
    var big = new AvlTree<long, float>();

    Test($"{bigCount:N0} timestamps inserted in order (limit 8000 ms)", () =>
    {
        var clock = Stopwatch.StartNew();
        for (long k = 0; k < bigCount; k++) big.Add(k * 5_000_000L, k);
        Console.WriteLine($"          took {clock.ElapsedMilliseconds} ms; height {big.Height} (an unbalanced tree would be {bigCount:N0})");
        Expect(clock.ElapsedMilliseconds < 8000, $"took {clock.ElapsedMilliseconds} ms");
        Expect(big.Height <= HeightLimit(bigCount), $"height {big.Height}");
    });

    Test("10,000 range queries of about 100 results each over 1,000,000 entries (limit 1500 ms)", () =>
    {
        var clock = Stopwatch.StartNew();
        long returned = 0;
        for (int i = 0; i < 10_000; i++)
        {
            long start = rng.NextInt64(0, (bigCount - 150L) * 5_000_000L);
            returned += big.Between(start, start + 99L * 5_000_000L).Count();
        }
        Console.WriteLine($"          took {clock.ElapsedMilliseconds} ms for {returned:N0} results");
        Expect(clock.ElapsedMilliseconds < 1500, $"took {clock.ElapsedMilliseconds} ms");
    });

    Test("10,000 counts of a window over 1,000,000 entries (limit 300 ms)", () =>
    {
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 10_000; i++)
        {
            long start = rng.NextInt64(0, (bigCount - 150L) * 5_000_000L);
            _ = big.CountBetween(start, start + 100_000L * 5_000_000L);
        }
        Console.WriteLine($"          took {clock.ElapsedMilliseconds} ms");
        Expect(clock.ElapsedMilliseconds < 300, $"took {clock.ElapsedMilliseconds} ms");
    });
}

Console.WriteLine($"\n{passed} passing, {failed} failing.");
return failed == 0 ? 0 : 1;
