#:project Smart-X.Api/Smart-X.Api.csproj
using System.Diagnostics;
using System.Numerics;
using Smart_X.Api.Network;

// The executable spec for MeshGraph, GraphTraversal, MinimumSpanningTree and ShortestPath.
//   dotnet run test-part3-graph.cs                 everything
//   dotnet run test-part3-graph.cs -- graph        one section: graph, traversal, mst, dijkstra, cross, perf
// TODO = not implemented yet, FAIL = implemented but wrong, PASS = done.

var sections = new HashSet<string>(args.Select(a => a.ToLowerInvariant()));
bool Wants(string name) => sections.Count == 0 || sections.Contains(name);
var rng = new Random(20261112);
int passed = 0, notPassing = 0;

void Section(string title) => Console.WriteLine($"\n== {title} ==");

void Test(string name, Action body)
{
    try
    {
        body();
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    catch (NotImplementedException)
    {
        notPassing++;
        Console.WriteLine($"  TODO  {name}");
    }
    catch (Exception ex)
    {
        notPassing++;
        Console.WriteLine($"  FAIL  {name}\n          {ex.GetType().Name}: {ex.Message}");
    }
}

void Expect(bool ok, string message = "expectation failed")
{
    if (!ok) throw new Exception(message);
}

void ExpectThrows<T>(Action action, string what) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    catch (NotImplementedException)
    {
        throw;
    }
    catch (Exception ex)
    {
        throw new Exception($"{what}: expected {typeof(T).Name} but got {ex.GetType().Name}");
    }

    throw new Exception($"{what}: expected {typeof(T).Name} but nothing was thrown");
}

bool Close(double a, double b) => Math.Abs(a - b) < 1e-9;

string Pair(string a, string b) =>
    string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0 ? $"{a.ToLowerInvariant()}|{b.ToLowerInvariant()}" : $"{b.ToLowerInvariant()}|{a.ToLowerInvariant()}";

MeshGraph Build(params (string A, string B, double W)[] edges)
{
    var g = new MeshGraph();
    foreach (var (a, b, _) in edges)
    {
        g.AddVertex(a);
        g.AddVertex(b);
    }
    foreach (var (a, b, w) in edges) g.AddEdge(a, b, w);
    return g;
}

MeshGraph ToGraph(List<string> vertices, List<(string A, string B, double W)> edges)
{
    var g = new MeshGraph();
    foreach (var v in vertices) g.AddVertex(v);
    foreach (var (a, b, w) in edges) g.AddEdge(a, b, w);
    return g;
}

int Find(int[] parent, int x)
{
    while (parent[x] != x)
    {
        parent[x] = parent[parent[x]];
        x = parent[x];
    }
    return x;
}

// Splits a small graph into its connected components and returns each one's vertices and edges.
List<(List<string> Vertices, List<(string A, string B, double W)> Edges)> SplitComponents(List<string> vertices, List<(string A, string B, double W)> edges)
{
    var index = vertices.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
    var uf = Enumerable.Range(0, vertices.Count).ToArray();
    foreach (var e in edges) uf[Find(uf, index[e.A])] = Find(uf, index[e.B]);

    return vertices
        .GroupBy(v => Find(uf, index[v]))
        .Select(g => (g.ToList(), edges.Where(e => Find(uf, index[e.A]) == g.Key).ToList()))
        .ToList();
}

// The cheapest spanning tree of one connected piece, found by trying every subset of edges. Tiny graphs only.
double BruteTreeWeight(List<string> vertices, List<(string A, string B, double W)> edges)
{
    int n = vertices.Count, need = n - 1;
    if (need == 0) return 0;

    var index = vertices.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
    var best = double.PositiveInfinity;

    for (int mask = 0; mask < 1 << edges.Count; mask++)
    {
        if (BitOperations.PopCount((uint)mask) != need) continue;

        var uf = Enumerable.Range(0, n).ToArray();
        bool acyclic = true;
        double sum = 0;
        for (int i = 0; i < edges.Count && acyclic; i++)
        {
            if ((mask >> i & 1) == 0) continue;
            int a = Find(uf, index[edges[i].A]), b = Find(uf, index[edges[i].B]);
            if (a == b) acyclic = false;
            else
            {
                uf[a] = b;
                sum += edges[i].W;
            }
        }
        if (acyclic) best = Math.Min(best, sum);
    }

    return best;
}

(List<string> Vertices, List<(string A, string B, double W)> Edges) RandomGraph(int n, double density, bool wholeWeights, int maxEdges)
{
    var vertices = Enumerable.Range(0, n).Select(i => $"N{i}").ToList();
    var edges = new List<(string, string, double)>();
    for (int i = 0; i < n; i++)
    {
        for (int j = i + 1; j < n && edges.Count < maxEdges; j++)
        {
            if (rng.NextDouble() >= density) continue;
            edges.Add((vertices[i], vertices[j], wholeWeights ? rng.Next(1, 6) : Math.Round(rng.NextDouble() * 10 + 0.1, 3)));
        }
    }
    return (vertices, edges);
}

// A graph that is always connected: a random path through every vertex plus some extra links.
(List<string> Vertices, List<(string A, string B, double W)> Edges) RandomConnectedGraph(int n, int extraEdges)
{
    var vertices = Enumerable.Range(0, n).Select(i => $"N{i}").ToList();
    var order = vertices.OrderBy(_ => rng.Next()).ToList();
    var edges = new List<(string, string, double)>();
    var seen = new HashSet<string>();

    void Add(string a, string b)
    {
        if (a == b || !seen.Add(Pair(a, b))) return;
        edges.Add((a, b, Math.Round(rng.NextDouble() * 10 + 0.1, 3)));
    }

    for (int i = 1; i < n; i++) Add(order[i - 1], order[i]);
    for (int i = 0; i < extraEdges; i++) Add(vertices[rng.Next(n)], vertices[rng.Next(n)]);
    return (vertices, edges);
}

// Checks an MST result: real edges, right weights, no cycles, and exactly the expected vertices spanned.
void ExpectSpanningTree(MeshGraph g, MstResult result, IReadOnlyCollection<string> spanned)
{
    var real = g.Edges.ToDictionary(e => Pair(e.A, e.B), e => e.Weight);
    var uf = spanned.Select((v, i) => (v, i)).ToDictionary(x => x.v.ToLowerInvariant(), x => x.i);
    var parent = Enumerable.Range(0, spanned.Count).ToArray();
    double sum = 0;

    foreach (var e in result.Edges)
    {
        Expect(real.TryGetValue(Pair(e.A, e.B), out var w) && Close(w, e.Weight), $"edge {e.A}-{e.B} is not in the graph with that weight");
        Expect(uf.ContainsKey(e.A.ToLowerInvariant()) && uf.ContainsKey(e.B.ToLowerInvariant()), $"edge {e.A}-{e.B} reaches outside the component");
        int a = Find(parent, uf[e.A.ToLowerInvariant()]), b = Find(parent, uf[e.B.ToLowerInvariant()]);
        Expect(a != b, $"edge {e.A}-{e.B} closes a cycle");
        parent[a] = b;
        sum += e.Weight;
    }

    Expect(result.Edges.Count == spanned.Count - 1, $"a tree over {spanned.Count} vertices needs {spanned.Count - 1} edges, got {result.Edges.Count}");
    Expect(Close(sum, result.TotalWeight), $"TotalWeight {result.TotalWeight} does not equal the sum of the edges {sum}");
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("graph"))
{
    Section("MeshGraph");

    Test("a new graph is empty", () =>
    {
        var g = new MeshGraph();
        Expect(g.VertexCount == 0 && g.EdgeCount == 0 && !g.Vertices.Any() && !g.Edges.Any());
    });

    Test("AddVertex says whether the vertex was new", () =>
    {
        var g = new MeshGraph();
        Expect(g.AddVertex("A"), "first add should return true");
        Expect(!g.AddVertex("A"), "second add should return false");
        Expect(g.VertexCount == 1);
    });

    Test("vertex ids are case-insensitive and keep the first spelling", () =>
    {
        var g = new MeshGraph();
        g.AddVertex("aa:bb");
        Expect(g.HasVertex("AA:BB"), "lookup should ignore case");
        Expect(!g.AddVertex("AA:BB"), "same id in another case is the same vertex");
        Expect(g.Vertices.Single() == "aa:bb", "should report the spelling it was first added with");
    });

    Test("an edge is undirected: both ends list each other", () =>
    {
        var g = Build(("A", "B", 2.5));
        Expect(g.Neighbors("A").Single() == ("B", 2.5), "A should list B");
        Expect(g.Neighbors("B").Single() == ("A", 2.5), "B should list A");
        Expect(g.HasEdge("A", "B") && g.HasEdge("b", "a"), "HasEdge should work in both directions and ignore case");
    });

    Test("AddEdge is true for a new edge and replaces the weight of an existing one", () =>
    {
        var g = new MeshGraph();
        g.AddVertex("A");
        g.AddVertex("B");
        Expect(g.AddEdge("A", "B", 1), "new edge should return true");
        Expect(!g.AddEdge("B", "A", 5), "the same edge the other way round is not new");
        Expect(g.EdgeCount == 1, "still one edge");
        Expect(g.Neighbors("A").Single().Weight == 5 && g.Neighbors("B").Single().Weight == 5, "weight should be replaced on both sides");
    });

    Test("AddEdge rejects bad input", () =>
    {
        var g = Build(("A", "B", 1));
        ExpectThrows<ArgumentException>(() => g.AddEdge("A", "Z", 1), "unknown vertex");
        ExpectThrows<ArgumentException>(() => g.AddEdge("A", "A", 1), "self-loop");
        ExpectThrows<ArgumentOutOfRangeException>(() => g.AddEdge("A", "B", -1), "negative weight");
        ExpectThrows<ArgumentOutOfRangeException>(() => g.AddEdge("A", "B", double.NaN), "NaN weight");
        ExpectThrows<ArgumentOutOfRangeException>(() => g.AddEdge("A", "B", double.PositiveInfinity), "infinite weight");
        g.AddVertex("C");
        Expect(g.AddEdge("A", "C", 0), "a zero weight is allowed");
    });

    Test("Edges lists every undirected edge exactly once", () =>
    {
        var g = Build(("A", "B", 1), ("B", "C", 2), ("C", "A", 3));
        var edges = g.Edges.ToList();
        Expect(edges.Count == 3 && g.EdgeCount == 3, $"expected 3 edges, got {edges.Count}");
        Expect(edges.Select(e => Pair(e.A, e.B)).Distinct().Count() == 3, "an edge was listed twice");
    });

    Test("RemoveEdge works once and updates both ends", () =>
    {
        var g = Build(("A", "B", 1), ("B", "C", 2));
        Expect(g.RemoveEdge("b", "a"), "should remove it");
        Expect(!g.RemoveEdge("A", "B"), "already gone");
        Expect(g.EdgeCount == 1 && !g.Neighbors("A").Any() && g.Neighbors("B").Count() == 1);
        Expect(g.VertexCount == 3, "the vertices stay");
    });

    Test("RemoveVertex also removes its edges", () =>
    {
        var g = Build(("HUB", "A", 1), ("HUB", "B", 1), ("HUB", "C", 1), ("A", "B", 1));
        Expect(g.RemoveVertex("hub"), "should remove it");
        Expect(!g.RemoveVertex("HUB"), "already gone");
        Expect(g.VertexCount == 3 && g.EdgeCount == 1, $"expected 3 vertices and 1 edge, got {g.VertexCount} and {g.EdgeCount}");
        Expect(g.Neighbors("A").Single().Neighbor == "B", "A should only have B left");
    });

    Test("Neighbors of an unknown vertex throws", () =>
    {
        ExpectThrows<ArgumentException>(() => new MeshGraph().Neighbors("A").ToList(), "unknown vertex");
    });

    Test("Without returns an independent copy minus the given vertices", () =>
    {
        var g = Build(("A", "B", 1), ("B", "C", 1), ("C", "D", 1));
        var copy = g.Without(new[] { "b", "not-in-graph" });
        Expect(copy.VertexCount == 3 && !copy.HasVertex("B"), "B should be gone from the copy");
        Expect(copy.EdgeCount == 1 && copy.HasEdge("C", "D"), "only C-D should survive");
        Expect(g.VertexCount == 4 && g.EdgeCount == 3, "the original must not change");
        copy.AddVertex("X");
        Expect(!g.HasVertex("X"), "changing the copy must not change the original");
    });

    Test("2000 random operations agree with a simple model", () =>
    {
        var g = new MeshGraph();
        var vertices = new HashSet<string>();
        var edges = new Dictionary<string, double>();
        string Id() => $"V{rng.Next(10)}";
        string Spell(string id) => rng.Next(2) == 0 ? id : id.ToLowerInvariant();

        for (int step = 0; step < 2000; step++)
        {
            string a = Id(), b = Id();
            switch (rng.Next(4))
            {
                case 0:
                    Expect(g.AddVertex(Spell(a)) == vertices.Add(a.ToLowerInvariant()), $"step {step}: AddVertex({a})");
                    break;
                case 1:
                    Expect(g.RemoveVertex(Spell(a)) == vertices.Remove(a.ToLowerInvariant()), $"step {step}: RemoveVertex({a})");
                    foreach (var key in edges.Keys.Where(k => k.Split('|').Contains(a.ToLowerInvariant())).ToList()) edges.Remove(key);
                    break;
                case 2:
                    if (a == b || !vertices.Contains(a.ToLowerInvariant()) || !vertices.Contains(b.ToLowerInvariant())) break;
                    double w = rng.Next(0, 9);
                    var isNew = !edges.ContainsKey(Pair(a, b));
                    Expect(g.AddEdge(Spell(a), Spell(b), w) == isNew, $"step {step}: AddEdge({a},{b})");
                    edges[Pair(a, b)] = w;
                    break;
                default:
                    Expect(g.RemoveEdge(Spell(a), Spell(b)) == edges.Remove(Pair(a, b)), $"step {step}: RemoveEdge({a},{b})");
                    break;
            }

            Expect(g.VertexCount == vertices.Count, $"step {step}: VertexCount {g.VertexCount} but model has {vertices.Count}");
            Expect(g.EdgeCount == edges.Count, $"step {step}: EdgeCount {g.EdgeCount} but model has {edges.Count}");
            var listed = g.Edges.ToDictionary(e => Pair(e.A, e.B), e => e.Weight);
            Expect(listed.Count == edges.Count && edges.All(kv => listed.TryGetValue(kv.Key, out var lw) && lw == kv.Value), $"step {step}: Edges does not match the model");
        }

        foreach (var v in vertices)
        {
            var expected = edges.Where(kv => kv.Key.Split('|').Contains(v)).Select(kv => (kv.Key.Split('|').Single(x => x != v), kv.Value)).OrderBy(x => x.Item1).ToList();
            var actual = g.Neighbors(v).Select(n => (n.Neighbor.ToLowerInvariant(), n.Weight)).OrderBy(x => x.Item1).ToList();
            Expect(expected.SequenceEqual(actual), $"Neighbors({v}) does not match the model");
        }
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("traversal"))
{
    Section("GraphTraversal");

    // A-B-C-D-E in a line, a shortcut A-D, and a separate island X-Y.
    MeshGraph Sample() => Build(("A", "B", 1), ("B", "C", 1), ("C", "D", 1), ("D", "E", 1), ("A", "D", 1), ("X", "Y", 1));

    Test("BFS counts the fewest hops and skips unreachable vertices", () =>
    {
        var r = GraphTraversal.Bfs(Sample(), "A");
        var expected = new Dictionary<string, int> { ["A"] = 0, ["B"] = 1, ["D"] = 1, ["C"] = 2, ["E"] = 2 };
        Expect(r.Hops.Count == 5 && expected.All(kv => r.Hops.TryGetValue(kv.Key, out var h) && h == kv.Value),
            "expected A0 B1 D1 C2 E2 and nothing for X and Y");
    });

    Test("BFS Order starts at the start, has each vertex once, and never goes back a level", () =>
    {
        var r = GraphTraversal.Bfs(Sample(), "A");
        Expect(r.Order[0] == "A", "the start comes first");
        Expect(r.Order.Count == 5 && r.Order.Distinct().Count() == 5, "each reachable vertex exactly once");
        for (int i = 1; i < r.Order.Count; i++) Expect(r.Hops[r.Order[i]] >= r.Hops[r.Order[i - 1]], "hop counts must not decrease along Order");
    });

    Test("BFS Parent is null for the start and one hop closer for everything else", () =>
    {
        var g = Sample();
        var r = GraphTraversal.Bfs(g, "A");
        Expect(r.Parent["A"] is null, "the start has no parent");
        foreach (var v in r.Order.Skip(1))
        {
            var p = r.Parent[v]!;
            Expect(g.HasEdge(p, v), $"{p} is not adjacent to {v}");
            Expect(r.Hops[v] == r.Hops[p] + 1, $"{v} should be one hop further than its parent {p}");
        }
    });

    Test("BFS PathTo gives the fewest-hop route", () =>
    {
        var r = GraphTraversal.Bfs(Sample(), "A");
        Expect(r.PathTo("E")!.SequenceEqual(new[] { "A", "D", "E" }), "A to E is A, D, E");
        Expect(r.PathTo("A")!.SequenceEqual(new[] { "A" }), "the start's route is just itself");
        Expect(r.PathTo("X") is null, "X is unreachable");
    });

    Test("BFS ignores weights: it counts hops, not cost", () =>
    {
        var r = GraphTraversal.Bfs(Build(("A", "C", 100), ("A", "B", 1), ("B", "C", 1)), "A");
        Expect(r.Hops["C"] == 1, "the direct link is one hop even though it is expensive");
    });

    Test("BFS hop counts match an independent calculation on 200 random graphs", () =>
    {
        for (int round = 0; round < 200; round++)
        {
            var (vertices, edges) = RandomGraph(rng.Next(2, 10), 0.3, wholeWeights: true, maxEdges: 30);
            var g = ToGraph(vertices, edges);
            var index = vertices.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
            int n = vertices.Count;

            var d = new double[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) d[i, j] = i == j ? 0 : double.PositiveInfinity;
            foreach (var (a, b, _) in edges) d[index[a], index[b]] = d[index[b], index[a]] = 1;
            for (int k = 0; k < n; k++) for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) d[i, j] = Math.Min(d[i, j], d[i, k] + d[k, j]);

            var start = vertices[rng.Next(n)];
            var r = GraphTraversal.Bfs(g, start);
            foreach (var target in vertices)
            {
                double expected = d[index[start], index[target]];
                if (double.IsInfinity(expected)) Expect(!r.Hops.ContainsKey(target), $"round {round}: {target} should be unreachable from {start}");
                else Expect(r.Hops.TryGetValue(target, out var hops) && hops == (int)expected, $"round {round}: {start} to {target} should be {expected} hops");
            }
        }
    });

    Test("BFS accepts any casing for the start and reports the stored spelling", () =>
    {
        var r = GraphTraversal.Bfs(Sample(), "a");
        Expect(r.Order[0] == "A");
    });

    Test("BFS and DFS throw for an unknown start", () =>
    {
        ExpectThrows<ArgumentException>(() => GraphTraversal.Bfs(Sample(), "nope"), "Bfs");
        ExpectThrows<ArgumentException>(() => GraphTraversal.Dfs(Sample(), "nope"), "Dfs");
    });

    Test("a down node cuts off what was only reachable through it", () =>
    {
        var g = Build(("G", "X", 1), ("X", "Y", 1), ("G", "Z", 1));
        var healthy = GraphTraversal.Bfs(g, "G");
        Expect(healthy.Hops.ContainsKey("Y"), "Y is reachable while X is up");

        var afterFailure = GraphTraversal.Bfs(g.Without(new[] { "X" }), "G");
        Expect(afterFailure.Order.OrderBy(v => v).SequenceEqual(new[] { "G", "Z" }), "only G and Z should be reachable once X is down");
    });

    Test("DFS reaches exactly the vertices BFS reaches", () =>
    {
        var g = Sample();
        var bfs = GraphTraversal.Bfs(g, "A").Order.OrderBy(v => v);
        var dfs = GraphTraversal.Dfs(g, "A").Order.OrderBy(v => v);
        Expect(bfs.SequenceEqual(dfs));
    });

    Test("DFS tree is consistent: parents are adjacent, visited earlier, one level up", () =>
    {
        var g = Sample();
        var r = GraphTraversal.Dfs(g, "A");
        Expect(r.Order[0] == "A" && r.Order.Count == 5 && r.Order.Distinct().Count() == 5, "start first, each vertex once");
        Expect(r.Parent["A"] is null && r.Hops["A"] == 0);
        foreach (var v in r.Order.Skip(1))
        {
            var p = r.Parent[v]!;
            Expect(g.HasEdge(p, v), $"{p} is not adjacent to {v}");
            Expect(r.Order.IndexOf(p) < r.Order.IndexOf(v), $"{p} should be visited before {v}");
            Expect(r.Hops[v] == r.Hops[p] + 1, $"depth of {v} should be one more than {p}");
        }
    });

    Test("DFS really goes deep first: every vertex's subtree is visited as one block right after it", () =>
    {
        // A tree: A has children B and C; B has D and E; C has F.
        var g = Build(("A", "B", 1), ("A", "C", 1), ("B", "D", 1), ("B", "E", 1), ("C", "F", 1));
        var r = GraphTraversal.Dfs(g, "A");
        var position = r.Order.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);

        foreach (var v in r.Order)
        {
            var subtree = r.Order.Where(u => { for (var x = u; x is not null; x = r.Parent[x]) if (x == v) return true; return false; }).ToList();
            var positions = subtree.Select(u => position[u]).OrderBy(i => i).ToList();
            Expect(positions.SequenceEqual(Enumerable.Range(position[v], subtree.Count)), $"the subtree under {v} is not contiguous, so this is not a depth-first order");
        }
    });

    Test("ConnectedComponents splits the graph into groups that can reach each other", () =>
    {
        var g = Sample();
        var parts = GraphTraversal.ConnectedComponents(g).Select(c => string.Join("", c.OrderBy(v => v))).OrderBy(s => s).ToList();
        Expect(parts.SequenceEqual(new[] { "ABCDE", "XY" }), $"got {string.Join(", ", parts)}");

        g.AddVertex("Z");
        Expect(GraphTraversal.ConnectedComponents(g).Count == 3, "an isolated vertex is its own component");
        Expect(GraphTraversal.ConnectedComponents(new MeshGraph()).Count == 0, "an empty graph has no components");
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("mst"))
{
    Section("MinimumSpanningTree");

    // The textbook graph from CLRS figure 23.1. Its minimum spanning tree weighs 37.
    MeshGraph Textbook() => Build(
        ("a", "b", 4), ("a", "h", 8), ("b", "c", 8), ("b", "h", 11), ("c", "d", 7), ("c", "f", 4), ("c", "i", 2),
        ("d", "e", 9), ("d", "f", 14), ("e", "f", 10), ("f", "g", 2), ("g", "h", 1), ("g", "i", 6), ("h", "i", 7));

    Test("Prim finds the textbook minimum (37) from every starting vertex", () =>
    {
        var g = Textbook();
        foreach (var root in g.Vertices.ToList())
        {
            var r = MinimumSpanningTree.Prim(g, root);
            Expect(Close(r.TotalWeight, 37), $"from {root}: expected 37, got {r.TotalWeight}");
            ExpectSpanningTree(g, r, g.Vertices.ToList());
        }
    });

    Test("Kruskal finds the textbook minimum (37)", () =>
    {
        var g = Textbook();
        var r = MinimumSpanningTree.Kruskal(g);
        Expect(Close(r.TotalWeight, 37), $"expected 37, got {r.TotalWeight}");
        ExpectSpanningTree(g, r, g.Vertices.ToList());
    });

    Test("a single vertex has an empty tree of weight 0", () =>
    {
        var g = new MeshGraph();
        g.AddVertex("A");
        Expect(MinimumSpanningTree.Prim(g, "A") is { Edges.Count: 0, TotalWeight: 0 }, "Prim");
        Expect(MinimumSpanningTree.Kruskal(g) is { Edges.Count: 0, TotalWeight: 0 }, "Kruskal");
    });

    Test("Prim spans only the root's component, Kruskal builds a forest", () =>
    {
        // Triangle A-B-C (tree costs 1+2 = 3) and a separate pair X-Y (cost 5).
        var g = Build(("A", "B", 1), ("B", "C", 2), ("A", "C", 3), ("X", "Y", 5));
        var fromA = MinimumSpanningTree.Prim(g, "A");
        Expect(fromA.Edges.Count == 2 && Close(fromA.TotalWeight, 3), $"from A: expected 2 edges and 3, got {fromA.Edges.Count} and {fromA.TotalWeight}");
        var fromX = MinimumSpanningTree.Prim(g, "X");
        Expect(fromX.Edges.Count == 1 && Close(fromX.TotalWeight, 5), "from X: just the one edge");
        var forest = MinimumSpanningTree.Kruskal(g);
        Expect(forest.Edges.Count == 3 && Close(forest.TotalWeight, 8), $"forest: expected 3 edges and 8, got {forest.Edges.Count} and {forest.TotalWeight}");
    });

    Test("when every weight is equal the tree costs (vertices - 1) x weight", () =>
    {
        var g = Build(("A", "B", 3), ("B", "C", 3), ("C", "D", 3), ("D", "A", 3), ("A", "C", 3), ("B", "D", 3));
        Expect(Close(MinimumSpanningTree.Prim(g, "A").TotalWeight, 9) && Close(MinimumSpanningTree.Kruskal(g).TotalWeight, 9));
    });

    Test("zero-weight edges are fine", () =>
    {
        var g = Build(("A", "B", 0), ("B", "C", 0), ("A", "C", 4));
        Expect(Close(MinimumSpanningTree.Prim(g, "C").TotalWeight, 0) && Close(MinimumSpanningTree.Kruskal(g).TotalWeight, 0));
    });

    Test("Prim throws for an unknown root", () =>
    {
        ExpectThrows<ArgumentException>(() => MinimumSpanningTree.Prim(Textbook(), "zz"), "unknown root");
    });

    Test("Prim and Kruskal match an exhaustive search on 300 random small graphs", () =>
    {
        for (int round = 0; round < 300; round++)
        {
            var (vertices, edges) = RandomGraph(rng.Next(2, 8), 0.5, wholeWeights: round % 2 == 0, maxEdges: 14);
            var g = ToGraph(vertices, edges);
            var parts = SplitComponents(vertices, edges);
            var best = parts.Select(p => BruteTreeWeight(p.Vertices, p.Edges)).ToList();

            var forest = MinimumSpanningTree.Kruskal(g);
            Expect(Close(forest.TotalWeight, best.Sum()), $"round {round}: Kruskal weighs {forest.TotalWeight}, the best forest is {best.Sum()}");
            Expect(forest.Edges.Count == vertices.Count - parts.Count, $"round {round}: a forest over {vertices.Count} vertices in {parts.Count} pieces needs {vertices.Count - parts.Count} edges");

            for (int i = 0; i < parts.Count; i++)
            {
                var root = parts[i].Vertices[rng.Next(parts[i].Vertices.Count)];
                var tree = MinimumSpanningTree.Prim(g, root);
                Expect(Close(tree.TotalWeight, best[i]), $"round {round}: Prim from {root} weighs {tree.TotalWeight}, the best is {best[i]}");
                ExpectSpanningTree(g, tree, parts[i].Vertices);
            }
        }
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("dijkstra"))
{
    Section("ShortestPath (Dijkstra)");

    // The six-vertex example from the Dijkstra article. From 1: 2=7, 3=9, 6=11, 4=20, 5=20.
    MeshGraph Textbook() => Build(("1", "2", 7), ("1", "3", 9), ("1", "6", 14), ("2", "3", 10), ("2", "4", 15), ("3", "4", 11), ("3", "6", 2), ("4", "5", 6), ("5", "6", 9));

    Test("finds the textbook distances and routes", () =>
    {
        var r = ShortestPath.Dijkstra(Textbook(), "1");
        var expected = new Dictionary<string, double> { ["1"] = 0, ["2"] = 7, ["3"] = 9, ["6"] = 11, ["4"] = 20, ["5"] = 20 };
        foreach (var (v, d) in expected) Expect(r.Distance.TryGetValue(v, out var got) && Close(got, d), $"distance to {v}: expected {d}");
        Expect(r.PathTo("5")!.SequenceEqual(new[] { "1", "3", "6", "5" }), "the route to 5 is 1, 3, 6, 5");
    });

    Test("the source costs 0 and has no parent", () =>
    {
        var r = ShortestPath.Dijkstra(Textbook(), "1");
        Expect(r.Distance["1"] == 0 && r.Parent["1"] is null);
        Expect(r.PathTo("1")!.SequenceEqual(new[] { "1" }));
    });

    Test("unreachable vertices are left out and have no route", () =>
    {
        var g = Build(("A", "B", 1), ("X", "Y", 1));
        var r = ShortestPath.Dijkstra(g, "A");
        Expect(!r.Distance.ContainsKey("X") && r.PathTo("X") is null && r.Distance.Count == 2);
    });

    Test("it takes a longer route when it is cheaper (cost, not hops)", () =>
    {
        var r = ShortestPath.Dijkstra(Build(("A", "C", 100), ("A", "B", 1), ("B", "C", 1)), "A");
        Expect(Close(r.Distance["C"], 2) && r.PathTo("C")!.SequenceEqual(new[] { "A", "B", "C" }));
    });

    Test("zero-weight edges are fine", () =>
    {
        var r = ShortestPath.Dijkstra(Build(("A", "B", 0), ("B", "C", 0), ("A", "C", 5)), "A");
        Expect(Close(r.Distance["C"], 0));
    });

    Test("throws for an unknown source", () =>
    {
        ExpectThrows<ArgumentException>(() => ShortestPath.Dijkstra(Textbook(), "zz"), "unknown source");
    });

    Test("every route is a real path whose edges add up to the reported distance", () =>
    {
        var (vertices, edges) = RandomConnectedGraph(12, 15);
        var g = ToGraph(vertices, edges);
        var weights = g.Edges.ToDictionary(e => Pair(e.A, e.B), e => e.Weight);
        var r = ShortestPath.Dijkstra(g, "N0");

        foreach (var v in vertices)
        {
            var path = r.PathTo(v)!;
            Expect(path[0] == "N0" && path[^1] == v, $"route to {v} should run N0..{v}");
            double cost = 0;
            for (int i = 1; i < path.Count; i++)
            {
                Expect(weights.TryGetValue(Pair(path[i - 1], path[i]), out var w), $"{path[i - 1]} and {path[i]} are not adjacent");
                cost += w;
            }
            Expect(Close(cost, r.Distance[v]), $"route to {v} costs {cost} but Distance says {r.Distance[v]}");
        }
    });

    Test("matches Floyd-Warshall on 300 random small graphs", () =>
    {
        for (int round = 0; round < 300; round++)
        {
            var (vertices, edges) = RandomGraph(rng.Next(2, 9), 0.4, wholeWeights: round % 3 == 0, maxEdges: 30);
            var g = ToGraph(vertices, edges);
            var index = vertices.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
            int n = vertices.Count;

            var d = new double[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) d[i, j] = i == j ? 0 : double.PositiveInfinity;
            foreach (var (a, b, w) in edges) d[index[a], index[b]] = d[index[b], index[a]] = Math.Min(d[index[a], index[b]], w);
            for (int k = 0; k < n; k++) for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) d[i, j] = Math.Min(d[i, j], d[i, k] + d[k, j]);

            foreach (var source in vertices)
            {
                var r = ShortestPath.Dijkstra(g, source);
                foreach (var target in vertices)
                {
                    double expected = d[index[source], index[target]];
                    if (double.IsInfinity(expected)) Expect(!r.Distance.ContainsKey(target), $"round {round}: {target} should be unreachable from {source}");
                    else Expect(r.Distance.TryGetValue(target, out var got) && Close(got, expected), $"round {round}: {source} to {target} expected {expected}");
                }
            }
        }
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("cross"))
{
    Section("Putting it together");

    Test("Dijkstra is never worse than the MST route, and sometimes strictly better", () =>
    {
        int strictlyBetter = 0;
        for (int round = 0; round < 300; round++)
        {
            var (vertices, edges) = RandomConnectedGraph(rng.Next(4, 10), rng.Next(3, 12));
            var g = ToGraph(vertices, edges);

            var mst = MinimumSpanningTree.Prim(g, "N0");
            var tree = new MeshGraph();
            foreach (var v in vertices) tree.AddVertex(v);
            foreach (var e in mst.Edges) tree.AddEdge(e.A, e.B, e.Weight);

            var routes = GraphTraversal.Bfs(tree, "N0");
            var cheapest = ShortestPath.Dijkstra(g, "N0");
            var treeCost = tree.Edges.ToDictionary(e => Pair(e.A, e.B), e => e.Weight);

            foreach (var v in vertices)
            {
                var path = routes.PathTo(v)!;
                double viaTree = 0;
                for (int i = 1; i < path.Count; i++) viaTree += treeCost[Pair(path[i - 1], path[i])];
                Expect(cheapest.Distance[v] <= viaTree + 1e-9, $"round {round}: Dijkstra to {v} costs {cheapest.Distance[v]}, more than the MST route {viaTree}");
                if (cheapest.Distance[v] < viaTree - 1e-9) strictlyBetter++;
            }

            double shortestTreeTotal = vertices.Where(v => v != "N0").Sum(v => g.Neighbors(v).First(n => n.Neighbor == cheapest.Parent[v]).Weight);
            Expect(shortestTreeTotal >= mst.TotalWeight - 1e-9, $"round {round}: the shortest-path tree costs {shortestTreeTotal}, less than the MST {mst.TotalWeight}");
        }
        Expect(strictlyBetter > 0, "across 300 graphs Dijkstra should beat the MST route at least once; if it never does, one of the algorithms is probably wrong");
    });

    Test("a 3x3 grid with a dead centre node still routes everything", () =>
    {
        var g = new MeshGraph();
        for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) g.AddVertex($"{x}{y}");
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                if (x < 2) g.AddEdge($"{x}{y}", $"{x + 1}{y}", 1);
                if (y < 2) g.AddEdge($"{x}{y}", $"{x}{y + 1}", 1);
            }
        }

        var alive = g.Without(new[] { "11" });
        var reach = GraphTraversal.Bfs(alive, "00");
        Expect(reach.Order.Count == 8, $"all 8 survivors should be reachable, got {reach.Order.Count}");
        Expect(Close(MinimumSpanningTree.Prim(alive, "00").TotalWeight, 7), "a tree over 8 devices with 1-cost links costs 7");
        Expect(ShortestPath.Dijkstra(alive, "00").PathTo("22")!.All(v => v != "11"), "no route may pass through the dead node");
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("perf"))
{
    Section("Scale");

    const int bigSize = 20_000;

    MeshGraph BuildBig()
    {
        var g = new MeshGraph();
        for (int i = 0; i < bigSize; i++) g.AddVertex($"V{i}");
        for (int i = 1; i < bigSize; i++) g.AddEdge($"V{i - 1}", $"V{i}", rng.Next(1, 100));
        for (int i = 0; i < 4 * bigSize; i++)
        {
            int a = rng.Next(bigSize), b = rng.Next(bigSize);
            if (a != b) g.AddEdge($"V{a}", $"V{b}", rng.Next(1, 100));
        }
        return g;
    }

    MeshGraph? bigGraph = null;
    MeshGraph Big() => bigGraph ??= BuildBig();

    void Timed(string name, int limitMs, Action body) => Test($"{name} (limit {limitMs} ms)", () =>
    {
        var sw = Stopwatch.StartNew();
        body();
        Console.WriteLine($"          took {sw.ElapsedMilliseconds} ms");
        Expect(sw.ElapsedMilliseconds <= limitMs, $"took {sw.ElapsedMilliseconds} ms");
    });

    Timed($"building a graph of {bigSize} vertices and about {5 * bigSize} edges", 4000, () => bigGraph = BuildBig());
    Timed("BFS over 20,000 vertices", 2000, () => Expect(GraphTraversal.Bfs(Big(), "V0").Order.Count == bigSize));
    Timed("Prim and Kruskal agree on 20,000 vertices", 4000, () =>
    {
        var prim = MinimumSpanningTree.Prim(Big(), "V0");
        var kruskal = MinimumSpanningTree.Kruskal(Big());
        Expect(prim.Edges.Count == bigSize - 1 && kruskal.Edges.Count == bigSize - 1, "both should span all vertices");
        Expect(Close(prim.TotalWeight, kruskal.TotalWeight), $"Prim {prim.TotalWeight} vs Kruskal {kruskal.TotalWeight}");
    });
    Timed("Dijkstra over 20,000 vertices", 3000, () => Expect(ShortestPath.Dijkstra(Big(), "V0").Distance.Count == bigSize));

    Console.WriteLine("\n  (next: a 20,000-vertex chain. If DFS is recursive this crashes the whole process, which is the point.)");
    Test("DFS survives a chain of 20,000 devices", () =>
    {
        var chain = new MeshGraph();
        for (int i = 0; i < 20_000; i++) chain.AddVertex($"C{i}");
        for (int i = 1; i < 20_000; i++) chain.AddEdge($"C{i - 1}", $"C{i}", 1);
        var r = GraphTraversal.Dfs(chain, "C0");
        Expect(r.Order.Count == 20_000 && r.Hops["C19999"] == 19_999, "should walk the whole chain");
    });
}

Console.WriteLine($"\n{passed} passing, {notPassing} not passing yet.");
return notPassing == 0 ? 0 : 1;
