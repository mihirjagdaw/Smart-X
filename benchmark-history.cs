#:project Smart-X.Api/Smart-X.Api.csproj
using System.Diagnostics;
using Smart_X.Api.History;

// Measures the AVL history tree against two alternatives as the history grows. Prints Markdown tables.
//   dotnet run benchmark-history.cs
// Window queries ask for the readings in a time range that holds about 100 of them (a few minutes of data at one
// reading every 5 seconds), at a random position in the history.

const long TickSpacing = 50_000_000L;   // 5 seconds, in 100 ns ticks
const int Window = 100;
var rng = new Random(7);

double Micros(Stopwatch sw, int runs) => sw.Elapsed.TotalMilliseconds * 1000.0 / runs;

// The alternatives -----------------------------------------------------------------------------------------
// 1. An unsorted list scanned from the start: what you get without any organisation.
List<(long Ticks, float Value)> ScanWindow(List<(long Ticks, float Value)> list, long from, long to)
{
    var found = new List<(long, float)>();
    for (int i = 0; i < list.Count; i++)
    {
        if (list[i].Ticks >= from && list[i].Ticks <= to) found.Add(list[i]);
    }
    return found;
}

// 2. A sorted array searched by halving: the best a static, append-only history can do.
int LowerBound(long[] keys, long target)
{
    int lo = 0, hi = keys.Length;
    while (lo < hi)
    {
        int mid = (lo + hi) / 2;
        if (keys[mid] < target) lo = mid + 1; else hi = mid;
    }
    return lo;
}

(long, float)[] ArrayWindow(long[] keys, float[] values, long from, long to)
{
    int start = LowerBound(keys, from), end = LowerBound(keys, to + 1);
    var found = new (long, float)[end - start];
    for (int i = start; i < end; i++) found[i - start] = (keys[i], values[i]);
    return found;
}

Console.WriteLine("### Window query: about 100 readings out of a growing history\n");
Console.WriteLine("| Readings stored | Tree height (AVL limit) | Build time | Memory per reading | AVL tree | Sorted array + binary search | Unsorted list scan | AVL vs scan |");
Console.WriteLine("|---:|---:|---:|---:|---:|---:|---:|---:|");

foreach (var n in new[] { 1_000, 10_000, 100_000, 1_000_000 })
{
    GC.Collect();
    var before = GC.GetTotalMemory(true);

    var build = Stopwatch.StartNew();
    var tree = new AvlTree<long, float>();
    for (int i = 0; i < n; i++) tree.Add(i * TickSpacing, i);
    build.Stop();

    var after = GC.GetTotalMemory(true);
    double bytesPerReading = (after - before) / (double)n;
    GC.KeepAlive(tree);

    var keys = Enumerable.Range(0, n).Select(i => i * TickSpacing).ToArray();
    var values = Enumerable.Range(0, n).Select(i => (float)i).ToArray();
    var list = keys.Select((k, i) => (k, (float)i)).ToList();

    int queries = 5000;
    int scanQueries = n >= 1_000_000 ? 100 : n >= 100_000 ? 400 : 2000;
    var starts = Enumerable.Range(0, queries).Select(_ => rng.Next(0, n - Window)).ToArray();

    // Warm up, so the first timing is not the JIT compiler.
    for (int i = 0; i < 200; i++)
    {
        var s = starts[i];
        _ = tree.Between(keys[s], keys[s + Window - 1]).ToList();
        _ = ArrayWindow(keys, values, keys[s], keys[s + Window - 1]);
        _ = ScanWindow(list, keys[s], keys[s + Window - 1]);
    }

    var avl = Stopwatch.StartNew();
    for (int i = 0; i < queries; i++)
    {
        var s = starts[i];
        if (tree.Between(keys[s], keys[s + Window - 1]).ToList().Count != Window) throw new Exception("AVL returned the wrong count");
    }
    avl.Stop();

    var sorted = Stopwatch.StartNew();
    for (int i = 0; i < queries; i++)
    {
        var s = starts[i];
        if (ArrayWindow(keys, values, keys[s], keys[s + Window - 1]).Length != Window) throw new Exception("array returned the wrong count");
    }
    sorted.Stop();

    var scan = Stopwatch.StartNew();
    for (int i = 0; i < scanQueries; i++)
    {
        var s = starts[i];
        if (ScanWindow(list, keys[s], keys[s + Window - 1]).Count != Window) throw new Exception("scan returned the wrong count");
    }
    scan.Stop();

    double avlUs = Micros(avl, queries), sortedUs = Micros(sorted, queries), scanUs = Micros(scan, scanQueries);
    var limit = (int)Math.Ceiling(1.45 * Math.Log2(n + 2));
    Console.WriteLine($"| {n:N0} | {tree.Height} ({limit}) | {build.ElapsedMilliseconds:N0} ms | {bytesPerReading:N0} bytes | {avlUs:N1} µs | {sortedUs:N1} µs | {scanUs:N1} µs | {scanUs / avlUs:N0}x faster |");
}

Console.WriteLine("\n### Counting the readings in a window (no results returned)\n");
Console.WriteLine("| Readings stored | AVL tree (two O(log n) walks) | Unsorted list scan |");
Console.WriteLine("|---:|---:|---:|");
foreach (var n in new[] { 10_000, 100_000, 1_000_000 })
{
    var tree = new AvlTree<long, float>();
    for (int i = 0; i < n; i++) tree.Add(i * TickSpacing, i);
    var list = Enumerable.Range(0, n).Select(i => (Ticks: i * TickSpacing, Value: (float)i)).ToList();
    long from = (long)(n * 0.25) * TickSpacing, to = (long)(n * 0.75) * TickSpacing;   // half of the whole history

    for (int i = 0; i < 100; i++) _ = tree.CountBetween(from, to);
    var avl = Stopwatch.StartNew();
    for (int i = 0; i < 10_000; i++) if (tree.CountBetween(from, to) != n / 2 + 1) throw new Exception("wrong count");
    avl.Stop();

    int scanRuns = n >= 1_000_000 ? 20 : 200;
    var scan = Stopwatch.StartNew();
    for (int r = 0; r < scanRuns; r++)
    {
        int c = 0;
        for (int i = 0; i < list.Count; i++) if (list[i].Ticks >= from && list[i].Ticks <= to) c++;
        if (c != n / 2 + 1) throw new Exception("wrong scan count");
    }
    scan.Stop();

    Console.WriteLine($"| {n:N0} | {Micros(avl, 10_000):N2} µs | {Micros(scan, scanRuns):N0} µs |");
}

Console.WriteLine("\n### Readings arriving out of order (delayed packets, backfills): inserting into the history\n");
Console.WriteLine("| Readings inserted | AVL tree | Sorted array (binary search + insert in place) |");
Console.WriteLine("|---:|---:|---:|");
foreach (var n in new[] { 10_000, 50_000, 200_000 })
{
    var shuffled = Enumerable.Range(0, n).Select(i => i * TickSpacing).OrderBy(_ => rng.Next()).ToArray();

    var avl = Stopwatch.StartNew();
    var tree = new AvlTree<long, float>();
    foreach (var k in shuffled) tree.Add(k, 0);
    avl.Stop();

    var arr = Stopwatch.StartNew();
    var sortedKeys = new List<long>(n);
    foreach (var k in shuffled)
    {
        int at = sortedKeys.BinarySearch(k);
        sortedKeys.Insert(~at, k);   // moves every later element along by one
    }
    arr.Stop();

    Console.WriteLine($"| {n:N0} | {avl.ElapsedMilliseconds:N0} ms | {arr.ElapsedMilliseconds:N0} ms |");
}
