using Smart_X.Shared.DTOs;

namespace Smart_X.Api.Network
{
    /// <summary>Turns a TopologyAnalysis into the responses the API sends.</summary>
    public static class TopologyReport
    {
        public static string Status(TopologyAnalysis a, MeshNode node) =>
            node.IsGateway ? "Gateway"
            : a.Down.Contains(node.Id) ? "Down"
            : !a.Reach.Hops.ContainsKey(node.Id) ? "Isolated"
            : a.Critical.Contains(node.Id) ? "Critical"
            : "Online";

        /// <summary>
        /// The whole picture. Root is where "who is connected?" is asked from (the gateway unless a device is
        /// given), and a subnet narrows the detail to one part of the estate. ArgumentException for a root that
        /// does not exist or is down.
        /// </summary>
        public static TopologyResponse Build(TopologyAnalysis a, IReadOnlyList<HealEvent> healLog, long version, string? subnet, string? root)
        {
            var rootId = MeshLayout.GatewayId;
            if (!string.IsNullOrWhiteSpace(root))
            {
                if (!a.Topology.ById.TryGetValue(root.Trim(), out var rootNode)) throw new ArgumentException($"Unknown device '{root}'.");
                rootId = rootNode.Id;
            }
            if (!a.Up.HasVertex(rootId)) throw new ArgumentException($"{rootId} is down, so nothing can be traced from it.");

            var fromRoot = rootId == MeshLayout.GatewayId ? a.Reach : GraphTraversal.Bfs(a.Up, rootId);
            var subnetName = string.IsNullOrWhiteSpace(subnet) ? null : subnet.Trim();

            var nodes = a.Topology.Nodes.Select(n => new TopologyNode(
                n.Id, n.Description, n.Category, n.Location, n.Zone, Math.Round(n.X, 1), Math.Round(n.Y, 1),
                Status(a, n),
                fromRoot.Hops.TryGetValue(n.Id, out var hops) ? hops : null,
                a.RouteHops.TryGetValue(n.Id, out var routeHops) ? routeHops : null,
                a.NextHop.TryGetValue(n.Id, out var next) ? next : null,
                a.RouteCost.TryGetValue(n.Id, out var cost) ? Math.Round(cost, 1) : null,
                subnetName is null || n.IsGateway || MeshLayout.InSubnet(n.Location, subnetName))).ToList();

            var links = a.Topology.Links.Select(l => new TopologyLink(
                l.A, l.B, Math.Round(l.DistanceMetres, 1), Math.Round(l.Cost, 1),
                a.MstEdgeKeys.Contains(l.Key), l.Backhaul, a.Down.Contains(l.A) || a.Down.Contains(l.B))).ToList();

            return new TopologyResponse(rootId, subnetName, Subnets(a), Summarise(a), SubnetStatus(a, fromRoot, subnetName), nodes, links, healLog.ToList(), version);
        }

        private static TopologySummary Summarise(TopologyAnalysis a)
        {
            var devices = a.Topology.Nodes.Count(n => !n.IsGateway);
            var mstTotal = a.Mst.TotalWeight;

            return new TopologySummary(
                devices,
                devices - a.Down.Count - a.CutOff.Count,
                a.Down.Count,
                a.CutOff.Count,
                a.Up.EdgeCount,
                a.Mst.Edges.Count,
                Math.Round(mstTotal, 1),
                Math.Round(a.AllLinksCost, 1),
                a.AllLinksCost > 0 ? Math.Round((a.AllLinksCost - mstTotal) / a.AllLinksCost * 100, 1) : 0,
                Math.Round(a.CollectCostViaMst, 1),
                Math.Round(a.CollectCostViaCheapest, 1),
                a.CollectCostViaCheapest > 0 ? Math.Round((a.CollectCostViaMst - a.CollectCostViaCheapest) / a.CollectCostViaCheapest * 100, 1) : 0,
                Math.Round(a.KruskalTotal, 1),
                a.MstVerified);
        }

        // Every part of the estate you can pick as a subnet: each facility and each zone.
        private static List<string> Subnets(TopologyAnalysis a) =>
            a.Topology.Nodes
                .Where(n => !n.IsGateway)
                .SelectMany(n => new[] { 1, 2 }.Select(depth => string.Join('/', MeshLayout.Segments(n.Location).Take(depth))))
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static SubnetDetail? SubnetStatus(TopologyAnalysis a, TraversalResult fromRoot, string? subnet)
        {
            if (subnet is null) return null;

            var members = a.Topology.Nodes
                .Where(n => !n.IsGateway && MeshLayout.InSubnet(n.Location, subnet))
                .Select(n => n.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var connected = members.Count(id => fromRoot.Hops.ContainsKey(id));
            var down = members.Count(a.Down.Contains);
            var cutOff = members.Count(id => a.Up.HasVertex(id) && !fromRoot.Hops.ContainsKey(id));

            // How many separate islands do the subnet's own healthy devices form if only they could talk to each other?
            var onlyMembers = a.Up.Without(a.Up.Vertices.Where(v => !members.Contains(v)).ToList());
            var islands = GraphTraversal.ConnectedComponents(onlyMembers).Count;

            return new SubnetDetail(subnet, members.Count, connected, down, cutOff, islands);
        }

        /// <summary>
        /// One device's route to the gateway: the power-efficient route along the backbone, the cheapest possible
        /// route for comparison, where it would hop if its next hop failed, and who its neighbours are.
        /// KeyNotFoundException for a device that is not in the mesh.
        /// </summary>
        public static RouteResponse Route(TopologyAnalysis a, string mac)
        {
            if (!a.Topology.ById.TryGetValue(mac.Trim(), out var node)) throw new KeyNotFoundException($"Device {mac} is not in the mesh.");

            var status = Status(a, node);
            var reachable = a.Reach.Hops.ContainsKey(node.Id);

            RoutePath? efficient = null, cheapest = null;
            double? extra = null;
            BackupRoute? backup = null;

            if (reachable && !node.IsGateway)
            {
                var steps = new List<string>();
                for (var current = a.NextHop[node.Id]; current is not null; current = a.NextHop[current]) steps.Add(current);
                efficient = ToPath(a, node.Id, steps, a.RouteCost[node.Id]);

                var cheapestSteps = a.Cheapest.PathTo(node.Id)!;
                cheapestSteps.Reverse();
                cheapestSteps.RemoveAt(0);
                cheapest = ToPath(a, node.Id, cheapestSteps, a.Cheapest.Distance[node.Id]);

                extra = cheapest.TotalCost > 0 ? Math.Round((efficient.TotalCost - cheapest.TotalCost) / cheapest.TotalCost * 100, 1) : 0;
                backup = BackupFor(a, node, a.NextHop[node.Id]!);
            }

            var neighbours = a.Topology.Graph.Neighbors(node.Id)
                .Select(n =>
                {
                    var other = a.Topology.ById[n.Neighbor];
                    var link = a.Topology.LinkByKey[MeshLink.LinkKey(node.Id, other.Id)];
                    return new NeighbourInfo(
                        other.Id, other.Description, Status(a, other), Math.Round(link.Cost, 1), Math.Round(link.DistanceMetres, 1),
                        a.NextHop.TryGetValue(other.Id, out var hop) ? hop : null,
                        a.RouteCost.TryGetValue(other.Id, out var routeCost) ? Math.Round(routeCost, 1) : null);
                })
                .OrderBy(n => n.LinkCost)
                .ToList();

            var message = status switch
            {
                "Gateway" => "The gateway: every route ends here.",
                "Down" => $"{node.Description} has gone silent, so it is out of the mesh until it reports again.",
                "Isolated" => $"{node.Description} is online, but every route to the gateway ran through devices that are down.",
                _ => $"{node.Description} reaches the gateway in {a.RouteHops[node.Id]} hop(s) along the power-efficient backbone."
            };

            return new RouteResponse(
                node.Id, node.Description, status, message,
                reachable ? a.Reach.Hops[node.Id] : null,
                efficient, cheapest, extra, backup, neighbours);
        }

        private static RoutePath ToPath(TopologyAnalysis a, string origin, List<string> steps, double total)
        {
            var hops = new List<RouteHop>();
            var previous = origin;
            foreach (var id in steps)
            {
                hops.Add(new RouteHop(id, a.Topology.ById[id].Description, Math.Round(a.Topology.LinkByKey[MeshLink.LinkKey(previous, id)].Cost, 1)));
                previous = id;
            }
            return new RoutePath(hops, Math.Round(total, 1));
        }

        // The route the device would take if its first hop stopped working: cut that hop out of the mesh and
        // find the cheapest way through what is left. When the first hop is the gateway itself, only that one link
        // is cut, since the gateway cannot fail.
        private static BackupRoute BackupFor(TopologyAnalysis a, MeshNode node, string firstHop)
        {
            MeshGraph without;
            if (string.Equals(firstHop, MeshLayout.GatewayId, StringComparison.OrdinalIgnoreCase))
            {
                without = a.Up.Without(Array.Empty<string>());
                without.RemoveEdge(node.Id, firstHop);
            }
            else
            {
                without = a.Up.Without(new[] { firstHop });
            }

            var alternative = ShortestPath.Dijkstra(without, MeshLayout.GatewayId);
            var path = alternative.PathTo(node.Id);
            if (path is null) return new BackupRoute(firstHop, null);

            path.Reverse();
            path.RemoveAt(0);
            return new BackupRoute(firstHop, ToPath(a, node.Id, path, alternative.Distance[node.Id]));
        }
    }
}
