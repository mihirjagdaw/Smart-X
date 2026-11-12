using Smart_X.Api.Processing;
using Smart_X.Shared;

namespace Smart_X.Api.Network
{
    public record MeshNode(string Id, string Description, SensorCategory? Category, string Location, string Zone, double X, double Y)
    {
        public bool IsGateway => Id == MeshLayout.GatewayId;
    }

    /// <summary>One radio link. Backhaul means longer than the normal radio range: a boosted link added to join islands.</summary>
    public record MeshLink(string A, string B, double DistanceMetres, double Cost, bool Backhaul)
    {
        public string Key => LinkKey(A, B);

        public static string LinkKey(string a, string b) =>
            string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0 ? $"{a}|{b}".ToLowerInvariant() : $"{b}|{a}".ToLowerInvariant();
    }

    /// <summary>The whole mesh when every device is healthy: the nodes, the links, and the graph made from them.</summary>
    public class MeshTopology
    {
        public MeshTopology(IReadOnlyList<MeshNode> nodes, IReadOnlyList<MeshLink> links, string fingerprint)
        {
            Nodes = nodes;
            Links = links;
            Fingerprint = fingerprint;
            ById = nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
            LinkByKey = links.ToDictionary(l => l.Key);

            Graph = new MeshGraph();
            foreach (var node in nodes) Graph.AddVertex(node.Id);
            foreach (var link in links) Graph.AddEdge(link.A, link.B, link.Cost);
        }

        public IReadOnlyList<MeshNode> Nodes { get; }

        public IReadOnlyList<MeshLink> Links { get; }

        public MeshGraph Graph { get; }

        public Dictionary<string, MeshNode> ById { get; }

        public Dictionary<string, MeshLink> LinkByKey { get; }

        /// <summary>Identifies the devices this topology was built from, so it is only rebuilt when they change.</summary>
        public string Fingerprint { get; }
    }

    public static class MeshBuilder
    {
        public static string Fingerprint(IEnumerable<DeviceState> devices) =>
            string.Join('\n', devices.OrderBy(d => d.Id).ThenBy(d => d.Mac, StringComparer.OrdinalIgnoreCase).Select(d => $"{d.Id}|{d.Mac}|{d.Location}"));

        /// <summary>
        /// Builds the mesh for these devices (pass only the active ones): every pair within radio range gets a
        /// link priced by the radio model, then any islands left over are joined by the cheapest backhaul links.
        /// </summary>
        public static MeshTopology Build(IReadOnlyList<DeviceState> devices, MeshOptions options)
        {
            var placement = MeshLayout.Place(devices, options);

            var nodes = new List<MeshNode>
            {
                new(MeshLayout.GatewayId, "Gateway", null, "Gateway", "Gateway", placement.Gateway.X, placement.Gateway.Y)
            };
            nodes.AddRange(devices
                .OrderBy(d => d.Id).ThenBy(d => d.Mac, StringComparer.OrdinalIgnoreCase)
                .Select(d => new MeshNode(d.Mac, d.Description, d.Category, d.Location, MeshLayout.Zone(d.Location), placement.Positions[d.Mac].X, placement.Positions[d.Mac].Y)));

            var links = new List<MeshLink>();

            // shortcut: every pair is measured, which is fine into the low thousands of devices (1,000 devices is
            // half a million distance checks); a spatial grid index would be the next step for a bigger estate.
            for (var i = 0; i < nodes.Count; i++)
            {
                for (var j = i + 1; j < nodes.Count; j++)
                {
                    var distance = Distance(nodes[i], nodes[j]);
                    if (distance <= options.RadioRangeMetres)
                    {
                        links.Add(new MeshLink(nodes[i].Id, nodes[j].Id, distance, options.HopCost(distance), Backhaul: false));
                    }
                }
            }

            links.AddRange(JoinIslands(nodes, links, options));
            return new MeshTopology(nodes, links, Fingerprint(devices));
        }

        // Finds the islands, measures the closest pair of devices between every two of them, and keeps the
        // cheapest set of those pairs that joins everything: a minimum spanning tree over the islands.
        private static List<MeshLink> JoinIslands(List<MeshNode> nodes, List<MeshLink> links, MeshOptions options)
        {
            var graph = new MeshGraph();
            foreach (var node in nodes) graph.AddVertex(node.Id);
            foreach (var link in links) graph.AddEdge(link.A, link.B, link.Cost);

            var islands = GraphTraversal.ConnectedComponents(graph);
            if (islands.Count < 2) return new List<MeshLink>();

            var islandOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < islands.Count; i++)
            {
                foreach (var id in islands[i]) islandOf[id] = i;
            }

            var closest = new Dictionary<(int, int), (double Distance, MeshNode A, MeshNode B)>();
            for (var i = 0; i < nodes.Count; i++)
            {
                for (var j = i + 1; j < nodes.Count; j++)
                {
                    int a = islandOf[nodes[i].Id], b = islandOf[nodes[j].Id];
                    if (a == b) continue;

                    var distance = Distance(nodes[i], nodes[j]);
                    var pair = (Math.Min(a, b), Math.Max(a, b));
                    if (!closest.TryGetValue(pair, out var best) || distance < best.Distance) closest[pair] = (distance, nodes[i], nodes[j]);
                }
            }

            var islandGraph = new MeshGraph();
            for (var i = 0; i < islands.Count; i++) islandGraph.AddVertex($"island{i}");
            foreach (var ((a, b), best) in closest) islandGraph.AddEdge($"island{a}", $"island{b}", options.HopCost(best.Distance));

            var joins = new List<MeshLink>();
            foreach (var edge in MinimumSpanningTree.Kruskal(islandGraph).Edges)
            {
                int a = int.Parse(edge.A["island".Length..]), b = int.Parse(edge.B["island".Length..]);
                var best = closest[(Math.Min(a, b), Math.Max(a, b))];
                joins.Add(new MeshLink(best.A.Id, best.B.Id, best.Distance, options.HopCost(best.Distance), Backhaul: true));
            }
            return joins;
        }

        private static double Distance(MeshNode a, MeshNode b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
