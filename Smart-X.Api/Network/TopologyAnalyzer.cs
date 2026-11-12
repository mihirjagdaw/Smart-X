using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Network
{
    /// <summary>
    /// Everything worked out about the mesh for one set of down devices. It is built once and then only read.
    /// "Up" is the mesh with the down devices removed.
    /// </summary>
    public class TopologyAnalysis
    {
        public required MeshTopology Topology { get; init; }

        public required HashSet<string> Down { get; init; }

        public required HashSet<string> Critical { get; init; }

        public required MeshGraph Up { get; init; }

        /// <summary>Breadth-first search from the gateway: who can still reach it, in how few hops.</summary>
        public required TraversalResult Reach { get; init; }

        /// <summary>Devices that are online but have no route left to the gateway.</summary>
        public required List<string> CutOff { get; init; }

        /// <summary>The power-efficient backbone: Prim's minimum spanning tree grown from the gateway.</summary>
        public required MstResult Mst { get; init; }

        public required HashSet<string> MstEdgeKeys { get; init; }

        /// <summary>Kruskal's tree over the same devices, as an independent check on Prim's total.</summary>
        public required double KruskalTotal { get; init; }

        public required bool MstVerified { get; init; }

        /// <summary>For each device, the next hop on its route along the backbone (null for the gateway).</summary>
        public required Dictionary<string, string?> NextHop { get; init; }

        public required Dictionary<string, int> RouteHops { get; init; }

        public required Dictionary<string, double> RouteCost { get; init; }

        /// <summary>Dijkstra from the gateway: the cheapest possible route to every device, for comparison.</summary>
        public required ShortestPaths Cheapest { get; init; }

        public double AllLinksCost { get; init; }

        public double CollectCostViaMst { get; init; }

        public double CollectCostViaCheapest { get; init; }
    }

    public static class TopologyAnalyzer
    {
        public static TopologyAnalysis Analyze(MeshTopology topology, IEnumerable<string> down, IEnumerable<string> critical)
        {
            var downSet = new HashSet<string>(down, StringComparer.OrdinalIgnoreCase);
            downSet.Remove(MeshLayout.GatewayId);

            var up = topology.Graph.Without(downSet);
            var reach = GraphTraversal.Bfs(up, MeshLayout.GatewayId);
            var cutOff = up.Vertices.Where(v => !reach.Hops.ContainsKey(v)).ToList();

            var mst = MinimumSpanningTree.Prim(up, MeshLayout.GatewayId);
            var kruskal = MinimumSpanningTree.Kruskal(up.Without(cutOff));
            var verified = Math.Abs(mst.TotalWeight - kruskal.TotalWeight) <= 1e-6 * Math.Max(1, mst.TotalWeight);

            // Each device's route is its path through the backbone tree. A tree has exactly one path between two
            // devices, so walking it breadth-first from the gateway gives every device its next hop and hop count.
            var tree = new MeshGraph();
            foreach (var id in reach.Order) tree.AddVertex(id);
            foreach (var edge in mst.Edges) tree.AddEdge(edge.A, edge.B, edge.Weight);
            var walk = GraphTraversal.Bfs(tree, MeshLayout.GatewayId);

            var edgeCost = mst.Edges.ToDictionary(e => MeshLink.LinkKey(e.A, e.B), e => e.Weight);
            var routeCost = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { [MeshLayout.GatewayId] = 0 };
            foreach (var id in walk.Order.Skip(1))
            {
                var parent = walk.Parent[id]!;
                routeCost[id] = routeCost[parent] + edgeCost[MeshLink.LinkKey(parent, id)];
            }

            var cheapest = ShortestPath.Dijkstra(up, MeshLayout.GatewayId);

            return new TopologyAnalysis
            {
                Topology = topology,
                Down = downSet,
                Critical = new HashSet<string>(critical, StringComparer.OrdinalIgnoreCase),
                Up = up,
                Reach = reach,
                CutOff = cutOff,
                Mst = mst,
                MstEdgeKeys = mst.Edges.Select(e => MeshLink.LinkKey(e.A, e.B)).ToHashSet(),
                KruskalTotal = kruskal.TotalWeight,
                MstVerified = verified,
                NextHop = walk.Parent,
                RouteHops = walk.Hops,
                RouteCost = routeCost,
                Cheapest = cheapest,
                AllLinksCost = up.Edges.Sum(e => e.Weight),
                CollectCostViaMst = routeCost.Values.Sum(),
                CollectCostViaCheapest = cheapest.Distance.Values.Sum()
            };
        }

        private const int MaxRerouteEvents = 8;

        /// <summary>
        /// What changed between two analyses, in words: devices that joined, left, went down or came back,
        /// devices cut off from or restored to the gateway, and devices whose route now goes another way.
        /// </summary>
        public static List<HealEvent> Diff(TopologyAnalysis? previous, TopologyAnalysis next, DateTimeOffset at)
        {
            var events = new List<HealEvent>();
            if (previous is null) return events;

            var before = previous.Topology.ById;
            var after = next.Topology.ById;
            string Name(string id) => after.TryGetValue(id, out var node) ? node.Description : before.TryGetValue(id, out var old) ? old.Description : id;
            void Add(string kind, string id, string message) => events.Add(new HealEvent(at, kind, id, message));

            var joined = after.Keys.Where(id => !before.ContainsKey(id)).OrderBy(id => id).ToList();
            var left = before.Keys.Where(id => !after.ContainsKey(id)).OrderBy(id => id).ToList();
            foreach (var id in joined) Add("Joined", id, $"{Name(id)} joined the mesh");
            foreach (var id in left) Add("Left", id, $"{Name(id)} left the mesh");

            foreach (var id in next.Down.Where(d => after.ContainsKey(d) && !previous.Down.Contains(d)).OrderBy(id => id)) Add("Down", id, $"{Name(id)} went down");
            foreach (var id in previous.Down.Where(d => after.ContainsKey(d) && !next.Down.Contains(d)).OrderBy(id => id)) Add("Up", id, $"{Name(id)} is back up");

            var wasCutOff = previous.CutOff.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var isCutOff = next.CutOff.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in isCutOff.Where(id => before.ContainsKey(id) && !wasCutOff.Contains(id)).OrderBy(id => id)) Add("CutOff", id, $"{Name(id)} has no route to the gateway any more");
            foreach (var id in wasCutOff.Where(id => after.ContainsKey(id) && next.Reach.Hops.ContainsKey(id)).OrderBy(id => id)) Add("Reconnected", id, $"{Name(id)} can reach the gateway again");

            // When devices joined or left the map is laid out afresh and every link cost shifts, so route changes
            // would only be noise; report them only when the same devices are still there.
            if (joined.Count == 0 && left.Count == 0)
            {
                var rerouted = next.NextHop
                    .Where(entry => entry.Value is not null
                        && previous.NextHop.TryGetValue(entry.Key, out var oldHop)
                        && !string.Equals(oldHop, entry.Value, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.Key)
                    .ToList();

                foreach (var entry in rerouted.Take(MaxRerouteEvents))
                {
                    Add("Rerouted", entry.Key, $"{Name(entry.Key)} now relays via {Name(entry.Value!)} (was via {Name(previous.NextHop[entry.Key]!)})");
                }
                if (rerouted.Count > MaxRerouteEvents) Add("Rerouted", "", $"{rerouted.Count - MaxRerouteEvents} more devices were re-routed");
            }

            return events;
        }
    }
}
