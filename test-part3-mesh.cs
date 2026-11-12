#:project Smart-X.Api/Smart-X.Api.csproj
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Smart_X.Api.Network;
using Smart_X.Api.Processing;
using Smart_X.Shared;
using Smart_X.Shared.DTOs;

// Checks the mesh model: layout, radio costs, building the graph, joining islands, the analysis (reach, backbone,
// cheapest routes), failures and the heal log, the report, and the background service.
//   dotnet run test-part3-mesh.cs                 everything
//   dotnet run test-part3-mesh.cs -- layout       one section: layout, builder, analysis, report, service, scale

var sections = new HashSet<string>(args.Select(a => a.ToLowerInvariant()));
bool Wants(string name) => sections.Count == 0 || sections.Contains(name);
var rng = new Random(20261114);
int passed = 0, failed = 0;
const string G = MeshLayout.GatewayId;
var options = new MeshOptions();

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

void ExpectThrows<T>(Action action, string what) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    catch (Exception ex) { throw new Exception($"{what}: expected {typeof(T).Name} but got {ex.GetType().Name}"); }
    throw new Exception($"{what}: expected {typeof(T).Name} but nothing was thrown");
}

bool Close(double a, double b) => Math.Abs(a - b) < 1e-6;

DeviceState Dev(int id, string mac, string location, SensorCategory category = SensorCategory.Environmental) =>
    new(mac, category, location, $"Device {mac}", true, Id: id);

List<DeviceState> Devices(int count, int zones)
{
    var list = new List<DeviceState>();
    for (int i = 0; i < count; i++) list.Add(Dev(i + 1, $"AA:{i:X4}", $"Estate/Block {(char)('A' + i % zones)}/Row {i / zones}"));
    return list;
}

// A hand-made network: just the links between named devices. Positions do not matter to the analysis.
MeshTopology Topo(Dictionary<string, string>? locations, params (string A, string B, double Cost)[] links)
{
    var ids = links.SelectMany(l => new[] { l.A, l.B }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var nodes = ids.Select(id =>
    {
        var where = locations is not null && locations.TryGetValue(id, out var loc) ? loc : "Estate/Block A";
        return new MeshNode(id, id, id == G ? null : SensorCategory.Environmental, where, MeshLayout.Zone(where), 0, 0);
    }).ToList();
    return new MeshTopology(nodes, links.Select(l => new MeshLink(l.A, l.B, 10, l.Cost, false)).ToList(), "test");
}

TopologyAnalysis Analyse(MeshTopology t, string[]? down = null, string[]? critical = null) =>
    TopologyAnalyzer.Analyze(t, down ?? Array.Empty<string>(), critical ?? Array.Empty<string>());

// ---------------------------------------------------------------------------------------------------------
if (Wants("layout"))
{
    Section("Layout and radio costs");

    Test("a zone is the first two parts of the location path", () =>
    {
        Expect(MeshLayout.Zone("Estate/Block A/Row 3") == "Estate/Block A");
        Expect(MeshLayout.Zone("Vineyard Block 1") == "Vineyard Block 1", "a one-part location is its own zone");
        Expect(MeshLayout.Zone("  Estate / Block B  ") == "Estate/Block B", "spaces around parts are ignored");
        Expect(MeshLayout.Zone("") == "Unassigned" && MeshLayout.Zone("/") == "Unassigned" && MeshLayout.Zone(null) == "Unassigned");
    });

    Test("a subnet matches whole path parts, ignoring case", () =>
    {
        Expect(MeshLayout.InSubnet("Estate/Block A/Row 3", "Estate/Block A"));
        Expect(MeshLayout.InSubnet("estate/block a/row 3", "Estate/Block A"), "case should not matter");
        Expect(MeshLayout.InSubnet("Estate/Block A", "Estate/Block A"), "a zone is inside itself");
        Expect(!MeshLayout.InSubnet("Estate/Block AB/Row 1", "Estate/Block A"), "Block AB is not Block A");
        Expect(!MeshLayout.InSubnet("Estate", "Estate/Block A"), "a shorter path is not inside a longer subnet");
        Expect(MeshLayout.InSubnet("Estate/Block A", ""), "an empty subnet is everything");
    });

    Test("positions are repeatable, unique, and do not depend on the order devices are listed in", () =>
    {
        var devices = Devices(60, 5);
        var first = MeshLayout.Place(devices, options);
        var second = MeshLayout.Place(devices.AsEnumerable().Reverse().ToList(), options);
        Expect(devices.All(d => first.Positions[d.Mac] == second.Positions[d.Mac]), "the same devices must get the same positions");
        Expect(first.Positions.Values.Distinct().Count() == 60, "no two devices may share a position");
        Expect(first.Gateway.X < first.Positions.Values.Min(p => p.X), "the gateway sits at the left edge");
    });

    Test("a hop costs 2 x electronics + amplifier x distance squared", () =>
    {
        Expect(Close(options.HopCost(0), 100), "at distance 0 only the electronics are left");
        Expect(Close(options.HopCost(80), 100 + 0.1 * 6400));
        Expect(options.HopCost(10) < options.HopCost(20) && options.HopCost(20) < options.HopCost(40), "costs rise with distance");
    });

    Test("beyond about 45 m two short hops beat one long hop, below that one hop wins", () =>
    {
        Expect(options.HopCost(30) < 2 * options.HopCost(15), "30 m: relaying through a midpoint costs more");
        Expect(2 * options.HopCost(30) < options.HopCost(60), "60 m: relaying through a midpoint costs less");
        var crossover = Math.Sqrt(4 * options.ElectronicsNjPerBit / options.AmplifierNjPerBitPerSquareMetre);
        Expect(Math.Abs(2 * options.HopCost(crossover / 2) - options.HopCost(crossover)) < 1e-6, $"the two should tie at {crossover:0.0} m");
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("builder"))
{
    Section("Building the mesh");

    Test("the mesh holds the gateway and every device, and each link is priced by distance", () =>
    {
        var devices = Devices(25, 3);
        var mesh = MeshBuilder.Build(devices, options);
        Expect(mesh.Nodes.Count == 26 && mesh.ById.ContainsKey(G), "25 devices plus the gateway");
        Expect(mesh.Links.Select(l => l.Key).Distinct().Count() == mesh.Links.Count, "no link may appear twice");
        foreach (var link in mesh.Links)
        {
            var a = mesh.ById[link.A];
            var b = mesh.ById[link.B];
            var distance = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            Expect(Close(link.DistanceMetres, distance), $"{link.A}-{link.B} distance");
            Expect(Close(link.Cost, options.HopCost(distance)), $"{link.A}-{link.B} cost");
        }
    });

    Test("devices within radio range are linked, and links longer than that are only joins between islands", () =>
    {
        var mesh = MeshBuilder.Build(Devices(30, 3), options);
        foreach (var link in mesh.Links.Where(l => !l.Backhaul)) Expect(link.DistanceMetres <= options.RadioRangeMetres, "a normal link must be in range");
        foreach (var link in mesh.Links.Where(l => l.Backhaul)) Expect(link.DistanceMetres > options.RadioRangeMetres, "a backhaul link is longer than the range");
        var nodes = mesh.Nodes;
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
            {
                double d = Math.Sqrt(Math.Pow(nodes[i].X - nodes[j].X, 2) + Math.Pow(nodes[i].Y - nodes[j].Y, 2));
                if (d <= options.RadioRangeMetres) Expect(mesh.Graph.HasEdge(nodes[i].Id, nodes[j].Id), $"{nodes[i].Id} and {nodes[j].Id} are {d:0.0} m apart and should be linked");
            }
    });

    Test("the mesh is always one connected network, for 0 to 200 devices in 1 to 8 zones", () =>
    {
        foreach (var (count, zones) in new[] { (0, 1), (1, 1), (2, 1), (5, 2), (40, 5), (80, 8), (200, 7), (30, 30) })
        {
            var mesh = MeshBuilder.Build(Devices(count, zones), options);
            var islands = GraphTraversal.ConnectedComponents(mesh.Graph).Count;
            Expect(islands == 1, $"{count} devices in {zones} zones formed {islands} islands");
        }
    });

    Test("with a tiny radio range every device is an island, and joining them gives the cheapest tree of links", () =>
    {
        var tiny = new MeshOptions { RadioRangeMetres = 10 };
        var devices = Devices(12, 3);
        var mesh = MeshBuilder.Build(devices, tiny);
        Expect(mesh.Links.All(l => l.Backhaul), "nothing is in range, so every link is a join");
        Expect(mesh.Links.Count == 12, "13 islands need 12 joins");

        // Independent answer: the cheapest tree over every pair of devices, found directly.
        var all = new MeshGraph();
        foreach (var n in mesh.Nodes) all.AddVertex(n.Id);
        foreach (var a in mesh.Nodes)
            foreach (var b in mesh.Nodes.Where(b => string.CompareOrdinal(a.Id, b.Id) < 0))
                all.AddEdge(a.Id, b.Id, tiny.HopCost(Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2))));
        Expect(Close(mesh.Links.Sum(l => l.Cost), MinimumSpanningTree.Kruskal(all).TotalWeight), "the joins should cost the same as the cheapest tree over all pairs");
    });

    Test("the fingerprint changes when a device is added, removed or moved, and not otherwise", () =>
    {
        var devices = Devices(10, 2);
        var baseline = MeshBuilder.Fingerprint(devices);
        Expect(baseline == MeshBuilder.Fingerprint(devices.AsEnumerable().Reverse()), "the order they are listed in does not matter");
        Expect(baseline != MeshBuilder.Fingerprint(devices.Append(Dev(99, "AA:9999", "Estate/Block A/Row 9"))), "added");
        Expect(baseline != MeshBuilder.Fingerprint(devices.Skip(1)), "removed");
        var moved = devices.Select(d => d.Mac == devices[3].Mac ? d with { Location = "Estate/Block B/Row 0" } : d);
        Expect(baseline != MeshBuilder.Fingerprint(moved), "moved to another location");
        Expect(baseline == MeshBuilder.Fingerprint(devices.Select(d => d with { LastValue = 5f })), "a new reading is not a change to the mesh");
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("analysis"))
{
    Section("Analysing the mesh");

    // The gateway and five devices in a ring.
    MeshTopology Ring() => Topo(null, (G, "A", 4), ("A", "B", 3), ("B", "C", 5), ("C", "D", 2), ("D", "E", 6), ("E", G, 7));

    Test("a healthy ring: everyone reaches the gateway and the backbone is a tree of 5 links", () =>
    {
        var a = Analyse(Ring());
        Expect(a.Reach.Order.Count == 6 && a.CutOff.Count == 0, "all six nodes reachable");
        Expect(a.Mst.Edges.Count == 5, "a tree over 6 nodes has 5 links");
        Expect(a.MstVerified && Close(a.Mst.TotalWeight, a.KruskalTotal), "Prim and Kruskal must agree");
        Expect(Close(a.Mst.TotalWeight, 4 + 3 + 5 + 2 + 6), "the ring minus its dearest link (7) is the backbone");
    });

    Test("each route is the chain of next hops and its cost is the sum of the links along it", () =>
    {
        var topology = Ring();
        var a = Analyse(topology);
        foreach (var id in new[] { "A", "B", "C", "D", "E" })
        {
            double cost = 0;
            var hops = 0;
            for (var current = id; a.NextHop[current] is { } next; current = next)
            {
                cost += topology.LinkByKey[MeshLink.LinkKey(current, next)].Cost;
                hops++;
            }
            Expect(Close(cost, a.RouteCost[id]), $"{id}: cost {cost} vs {a.RouteCost[id]}");
            Expect(hops == a.RouteHops[id], $"{id}: hops {hops} vs {a.RouteHops[id]}");
        }
        Expect(a.NextHop[G] is null && a.RouteCost[G] == 0);
    });

    Test("the cheapest route is never dearer than the backbone route, and the cheapest-route tree never costs less overall", () =>
    {
        for (int round = 0; round < 100; round++)
        {
            var devices = Devices(rng.Next(3, 60), rng.Next(1, 7));
            var a = Analyse(MeshBuilder.Build(devices, options));
            foreach (var id in a.RouteCost.Keys)
                Expect(a.Cheapest.Distance[id] <= a.RouteCost[id] + 1e-6, $"round {round}: {id} cheapest {a.Cheapest.Distance[id]} vs backbone {a.RouteCost[id]}");
            Expect(a.CollectCostViaCheapest <= a.CollectCostViaMst + 1e-6, $"round {round}: collecting via the cheapest routes cannot cost more");
            Expect(a.MstVerified, $"round {round}: Prim and Kruskal disagree");
            Expect(a.Mst.TotalWeight <= a.AllLinksCost + 1e-6, "the backbone is part of the links");
        }
    });

    Test("a device going down is removed, and the others reach the gateway the other way round", () =>
    {
        var before = Analyse(Ring());
        var after = Analyse(Ring(), down: new[] { "A" });
        Expect(!after.Up.HasVertex("A") && after.Down.Contains("A"));
        Expect(after.CutOff.Count == 0, "B, C, D and E can still go round the other side");
        foreach (var id in new[] { "B", "C", "D", "E" })
        {
            Expect(after.Reach.Hops.ContainsKey(id), $"{id} must still reach the gateway");
            Expect(after.NextHop[id] != "A", $"{id} must not route through the dead device");
        }
        Expect(before.NextHop["B"] == "A" && after.NextHop["B"] != "A", "B used to go through A");
    });

    Test("a device behind the failure is cut off, and comes back when the failed device does", () =>
    {
        MeshTopology Chain() => Topo(null, (G, "A", 1), ("A", "B", 1), ("B", "C", 1));
        var broken = Analyse(Chain(), down: new[] { "B" });
        Expect(broken.CutOff.SequenceEqual(new[] { "C" }), "C is online but has no way out");
        Expect(broken.Reach.Hops.ContainsKey("A") && !broken.Reach.Hops.ContainsKey("C"));

        var events = TopologyAnalyzer.Diff(Analyse(Chain()), broken, DateTimeOffset.UtcNow);
        Expect(events.Any(e => e.Kind == "Down" && e.Mac == "B") && events.Any(e => e.Kind == "CutOff" && e.Mac == "C"), "expected Down for B and CutOff for C");

        var healed = TopologyAnalyzer.Diff(broken, Analyse(Chain()), DateTimeOffset.UtcNow);
        Expect(healed.Any(e => e.Kind == "Up" && e.Mac == "B") && healed.Any(e => e.Kind == "Reconnected" && e.Mac == "C"), "expected Up for B and Reconnected for C");
    });

    Test("a failure in a ring logs the device that went down and who now relays through whom", () =>
    {
        var events = TopologyAnalyzer.Diff(Analyse(Ring()), Analyse(Ring(), down: new[] { "A" }), DateTimeOffset.UtcNow);
        Expect(events.Any(e => e.Kind == "Down" && e.Mac == "A"));
        var reroute = events.Single(e => e.Kind == "Rerouted" && e.Mac == "B");
        Expect(reroute.Message.Contains("was via A"), reroute.Message);
    });

    Test("nothing is logged when nothing changed, or for the very first analysis", () =>
    {
        var a = Analyse(Ring());
        Expect(TopologyAnalyzer.Diff(a, Analyse(Ring()), DateTimeOffset.UtcNow).Count == 0, "same mesh, same state");
        Expect(TopologyAnalyzer.Diff(null, a, DateTimeOffset.UtcNow).Count == 0, "the first analysis has nothing to compare with");

        var chain = new[] { (G, "A", 1.0), ("A", "B", 1.0), ("B", "C", 1.0) };
        var cutOff = Analyse(Topo(null, chain), down: new[] { "B" });
        var stillCutOff = Analyse(Topo(null, chain), down: new[] { "B" }, critical: new[] { "A" });
        Expect(TopologyAnalyzer.Diff(cutOff, stillCutOff, DateTimeOffset.UtcNow).Count == 0, "C stays cut off, so there is nothing to report");
    });

    Test("adding a device logs it joining, without a flood of route changes", () =>
    {
        var before = Analyse(Ring());
        // F is so close to the gateway that B now relays through it: that is a side effect of joining, not news.
        var bigger = Analyse(Topo(null, (G, "A", 4), ("A", "B", 3), ("B", "C", 5), ("C", "D", 2), ("D", "E", 6), ("E", G, 7), (G, "F", 0.5), ("F", "B", 0.5)));
        Expect(bigger.NextHop["B"] == "F", "setup: B should now relay through F");
        var events = TopologyAnalyzer.Diff(before, bigger, DateTimeOffset.UtcNow);
        Expect(events.Count == 1 && events[0].Kind == "Joined" && events[0].Mac == "F", string.Join(" | ", events.Select(e => e.Message)));
    });

    Test("when many devices change route, only the first 8 are listed and the rest are summarised", () =>
    {
        // 20 leaves each linked to relay A (cheap) and relay B (dear). A is on the gateway; so is B.
        var links = new List<(string, string, double)> { (G, "A", 1), (G, "B", 1) };
        for (int i = 0; i < 20; i++)
        {
            links.Add(($"L{i:00}", "A", 1));
            links.Add(($"L{i:00}", "B", 5));
        }
        var events = TopologyAnalyzer.Diff(Analyse(Topo(null, links.ToArray())), Analyse(Topo(null, links.ToArray()), down: new[] { "A" }), DateTimeOffset.UtcNow);
        var rerouted = events.Where(e => e.Kind == "Rerouted").ToList();
        Expect(rerouted.Count == 9, $"expected 8 listed plus a summary, got {rerouted.Count}");
        Expect(rerouted.Last().Message.StartsWith("12 more"), rerouted.Last().Message);
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("report"))
{
    Section("The report");

    // Block A has A1 and A2, Block B has B1, and A1 and A2 can only reach each other through B1.
    var places = new Dictionary<string, string> { ["A1"] = "Estate/Block A/Row 1", ["A2"] = "Estate/Block A/Row 2", ["B1"] = "Estate/Block B/Row 1" };
    MeshTopology Split() => Topo(places, (G, "A1", 2), ("A1", "B1", 2), ("B1", "A2", 2), ("A2", G, 9));

    Test("every device gets a status, links on the backbone are flagged, and the counts add up", () =>
    {
        var a = Analyse(Split(), down: new[] { "B1" }, critical: new[] { "A1" });
        var r = TopologyReport.Build(a, Array.Empty<HealEvent>(), 1, null, null);
        var status = r.Nodes.ToDictionary(n => n.Id, n => n.Status);
        Expect(status[G] == "Gateway" && status["B1"] == "Down" && status["A1"] == "Critical" && status["A2"] == "Online", string.Join(", ", status.Select(s => $"{s.Key}={s.Value}")));
        Expect(r.Links.Count(l => l.OnMst) == a.Mst.Edges.Count, "flagged links must be the backbone");
        Expect(r.Links.Where(l => l.A == "B1" || l.B == "B1").All(l => l.Down), "links to a down device are flagged down");
        var s = r.Summary;
        Expect(s.Nodes == 3 && s.Online + s.Down + s.Isolated == s.Nodes, $"{s.Online} online + {s.Down} down + {s.Isolated} isolated should make {s.Nodes}");
        Expect(s.MstVerified && Close(s.SavingPercent, Math.Round((s.AllLinksCost - s.MstTotalCost) / s.AllLinksCost * 100, 1)));
    });

    Test("asking from a device traces hops from that device, and bad roots are rejected", () =>
    {
        var a = Analyse(Split());
        var fromA2 = TopologyReport.Build(a, Array.Empty<HealEvent>(), 1, null, "A2");
        var hops = fromA2.Nodes.ToDictionary(n => n.Id, n => n.HopsFromRoot);
        Expect(hops["A2"] == 0 && hops["B1"] == 1 && hops[G] == 1 && hops["A1"] == 2, string.Join(", ", hops.Select(h => $"{h.Key}={h.Value}")));
        ExpectThrows<ArgumentException>(() => TopologyReport.Build(a, Array.Empty<HealEvent>(), 1, null, "nope"), "unknown root");
        ExpectThrows<ArgumentException>(() => TopologyReport.Build(Analyse(Split(), down: new[] { "A2" }), Array.Empty<HealEvent>(), 1, null, "A2"), "root that is down");
    });

    Test("a subnet reports its members, how many are connected, down or cut off, and how many islands they form", () =>
    {
        var healthy = TopologyReport.Build(Analyse(Split()), Array.Empty<HealEvent>(), 1, "Estate/Block A", null).SubnetDetail!;
        Expect(healthy.Members == 2 && healthy.Connected == 2 && healthy.Down == 0 && healthy.CutOff == 0, "healthy Block A");
        Expect(healthy.Islands == 2, "A1 and A2 only reach each other through Block B, so on their own they are 2 islands");

        var broken = TopologyReport.Build(Analyse(Split(), down: new[] { "B1", "A1" }), Array.Empty<HealEvent>(), 1, "Estate/Block A", null).SubnetDetail!;
        Expect(broken.Down == 1 && broken.Connected == 1, "A1 down, A2 still reaches the gateway by its own link");
    });

    Test("the subnet list offers every facility and every zone", () =>
    {
        var r = TopologyReport.Build(Analyse(Split()), Array.Empty<HealEvent>(), 1, null, null);
        Expect(r.Subnets.SequenceEqual(new[] { "Estate", "Estate/Block A", "Estate/Block B" }), string.Join(", ", r.Subnets));
        var narrowed = TopologyReport.Build(Analyse(Split()), Array.Empty<HealEvent>(), 1, "Estate/Block B", null);
        Expect(narrowed.Nodes.Where(n => n.InSubnet).Select(n => n.Id).OrderBy(x => x).SequenceEqual(new[] { "B1", G }), "only Block B and the gateway are in the subnet");
    });

    Test("a route lists each hop with its cost, compares with the cheapest route, and offers a backup", () =>
    {
        // G-A-C is 3, G-B-C is 4: C's backbone route is the cheaper side.
        var a = Analyse(Topo(null, (G, "A", 1), ("A", "C", 2), (G, "B", 2), ("B", "C", 2)));
        var route = TopologyReport.Route(a, "C");
        Expect(route.Status == "Online" && route.FewestHops == 2);
        Expect(route.PowerEfficientRoute!.Hops.Select(h => h.Id).SequenceEqual(new[] { "A", G }) && Close(route.PowerEfficientRoute.TotalCost, 3), "C goes via A");
        Expect(Close(route.CheapestRoute!.TotalCost, 3) && route.ExtraPercentViaMst == 0, "here the backbone route is also the cheapest");
        Expect(route.Backup!.FailedHop == "A" && route.Backup.Route!.Hops.Select(h => h.Id).SequenceEqual(new[] { "B", G }) && Close(route.Backup.Route.TotalCost, 4), "if A fails, C goes via B");
        Expect(route.Neighbours.Select(n => n.Id).SequenceEqual(new[] { "A", "B" }) || route.Neighbours.Count == 2, "C has two neighbours");
    });

    Test("the backbone route can cost more than the cheapest route, and the report says by how much", () =>
    {
        // The backbone keeps the cheap links A-B (1) and B-C (1) and drops G-C (3), so C goes G-A-B-C = 1+1+1... but
        // the direct G-C link is 2.5 in a graph where the tree prefers three cheap hops: build the case explicitly.
        var a = Analyse(Topo(null, (G, "A", 2), ("A", "B", 2), ("B", "C", 2), (G, "C", 5)));
        var route = TopologyReport.Route(a, "C");
        Expect(Close(route.PowerEfficientRoute!.TotalCost, 6) && Close(route.CheapestRoute!.TotalCost, 5), $"backbone {route.PowerEfficientRoute!.TotalCost}, cheapest {route.CheapestRoute!.TotalCost}");
        Expect(route.ExtraPercentViaMst == 20, $"6 is 20% more than 5, got {route.ExtraPercentViaMst}");
    });

    Test("a device with no way round has no backup, and a device on the gateway only loses that one link", () =>
    {
        var chain = Analyse(Topo(null, (G, "A", 1), ("A", "B", 1)));
        Expect(TopologyReport.Route(chain, "B").Backup!.Route is null, "B has only A to go through");
        Expect(TopologyReport.Route(chain, "A").Backup!.Route is null && TopologyReport.Route(chain, "A").Backup!.FailedHop == G, "A's only link is the gateway");

        var square = Analyse(Topo(null, (G, "A", 1), (G, "B", 1), ("A", "B", 1)));
        var backup = TopologyReport.Route(square, "A").Backup!;
        Expect(backup.FailedHop == G && backup.Route!.Hops.Select(h => h.Id).SequenceEqual(new[] { "B", G }), "A can reach the gateway through B instead of directly");
    });

    Test("a down or cut-off device explains itself and has no route", () =>
    {
        var a = Analyse(Topo(null, (G, "A", 1), ("A", "B", 1), ("B", "C", 1)), down: new[] { "B" });
        var down = TopologyReport.Route(a, "B");
        Expect(down.Status == "Down" && down.PowerEfficientRoute is null && down.Message.Contains("gone silent"));
        Expect(down.Neighbours.Select(n => n.Id).OrderBy(x => x).SequenceEqual(new[] { "A", "C" }), "neighbours are still listed so you can see who is around it");
        var isolated = TopologyReport.Route(a, "C");
        Expect(isolated.Status == "Isolated" && isolated.PowerEfficientRoute is null && isolated.Message.Contains("down"));
        ExpectThrows<KeyNotFoundException>(() => TopologyReport.Route(a, "ZZ"), "unknown device");
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("service"))
{
    Section("The background service");

    TopologyService NewService(DeviceRegistry registry, AlertTracker alerts) =>
        new(registry, alerts, options, NullLogger<TopologyService>.Instance);

    Test("it only does work when something changed, reuses the layout for a failure, and logs what happened", () =>
    {
        var registry = new DeviceRegistry();
        var alerts = new AlertTracker(new ActionHistory());
        foreach (var d in Devices(20, 3))
            registry.Upsert(new Smart_X.Api.Data.SensorProfile(d.Id, d.Mac, d.Location, d.Category, d.Description, DateTimeOffset.UtcNow, true));
        var service = NewService(registry, alerts);

        Expect(service.Refresh(), "the first refresh builds the analysis");
        var first = service.Current;
        Expect(!service.Refresh() && ReferenceEquals(first, service.Current), "nothing changed, so the same analysis stays");
        Expect(service.HealLog().Count == 0, "no events yet");

        alerts.TryRaise("AA:0005", AlertType.Disconnected, "silent");
        Expect(service.Refresh(), "a device going down changes the analysis");
        Expect(ReferenceEquals(first.Topology, service.Current.Topology), "but the layout and links are reused");
        Expect(service.Current.Down.Contains("AA:0005") && service.HealLog().Any(e => e.Kind == "Down" && e.Mac == "AA:0005"), "and the heal log says so");
        Expect(service.HealLog()[0].Kind == "Down", "the cause is listed above the routes that moved because of it");

        alerts.Clear("AA:0005", AlertType.Disconnected, "back");
        Expect(service.Refresh() && service.HealLog()[0].Kind is "Up" or "Reconnected" or "Rerouted", "coming back is logged, newest first");

        var d2 = Dev(99, "AA:9999", "Estate/Block A/Row 9");
        registry.Upsert(new Smart_X.Api.Data.SensorProfile(99, d2.Mac, d2.Location, d2.Category, d2.Description, DateTimeOffset.UtcNow, true));
        Expect(service.Refresh() && !ReferenceEquals(first.Topology, service.Current.Topology), "a new device rebuilds the layout");
        Expect(service.HealLog().Any(e => e.Kind == "Joined" && e.Mac == "AA:9999"));
        Expect(service.Version == 4, $"version {service.Version}");
    });

    Test("deactivated devices leave the mesh, and a down device that is deactivated stops being reported down", () =>
    {
        var registry = new DeviceRegistry();
        var alerts = new AlertTracker(new ActionHistory());
        foreach (var d in Devices(6, 1))
            registry.Upsert(new Smart_X.Api.Data.SensorProfile(d.Id, d.Mac, d.Location, d.Category, d.Description, DateTimeOffset.UtcNow, true));
        var service = NewService(registry, alerts);
        service.Refresh();
        Expect(service.Current.Topology.Nodes.Count == 7);

        registry.Upsert(new Smart_X.Api.Data.SensorProfile(1, "AA:0000", "Estate/Block A/Row 0", SensorCategory.Environmental, "Device AA:0000", DateTimeOffset.UtcNow, false));
        service.Refresh();
        Expect(service.Current.Topology.Nodes.Count == 6 && !service.Current.Topology.ById.ContainsKey("AA:0000"), "an inactive device is not in the mesh");
    });
}

// ---------------------------------------------------------------------------------------------------------
if (Wants("scale"))
{
    Section("Scale");

    foreach (var (count, zones, limitMs) in new[] { (200, 10, 3000), (1000, 20, 6000), (2000, 25, 20000) })
    {
        Test($"{count:N0} devices in {zones} zones: build, analyse, and a failure (limit {limitMs} ms)", () =>
        {
            var devices = Devices(count, zones);
            var clock = Stopwatch.StartNew();
            var mesh = MeshBuilder.Build(devices, options);
            var built = clock.ElapsedMilliseconds;

            clock.Restart();
            var healthy = Analyse(mesh);
            var analysed = clock.ElapsedMilliseconds;

            clock.Restart();
            var failure = Analyse(mesh, down: new[] { devices[count / 2].Mac });
            var events = TopologyAnalyzer.Diff(healthy, failure, DateTimeOffset.UtcNow);
            var reacted = clock.ElapsedMilliseconds;

            Console.WriteLine($"          {mesh.Links.Count:N0} links | build {built} ms | analyse {analysed} ms | failure + heal log {reacted} ms ({events.Count} events)");
            Expect(healthy.CutOff.Count == 0 && healthy.MstVerified, "the healthy mesh is fully connected and Prim matches Kruskal");
            Expect(built + analysed + reacted <= limitMs, $"took {built + analysed + reacted} ms");
        });
    }
}

Console.WriteLine($"\n{passed} passing, {failed} failing.");
return failed == 0 ? 0 : 1;
