namespace Smart_X.Api.Network
{
    /// <summary>
    /// What a traversal found. Only vertices reachable from the start appear in these collections.
    ///  - Order: the order vertices were visited, start first.
    ///  - Hops: BFS = fewest hops from the start; DFS = depth in the DFS tree.
    ///  - Parent: the vertex each one was reached from. The start's parent is null.
    /// </summary>
    public record TraversalResult(List<string> Order, Dictionary<string, int> Hops, Dictionary<string, string?> Parent)
    {
        /// <summary>The route start..vertex by following Parent, or null when the vertex was not reached.</summary>
        public List<string>? PathTo(string vertex) => RouteTo(Parent, vertex);

        // Walks from the target back to the start through the parent links, then flips it into start-to-target order.
        internal static List<string>? RouteTo(Dictionary<string, string?> parent, string target)
        {
            if (!parent.ContainsKey(target)) return null;

            var route = new List<string>();
            for (string? step = target; step is not null; step = parent[step]) route.Add(step);
            route.Reverse();
            return route;
        }
    }

    /// <summary>
    /// Traversals over a MeshGraph. All three throw ArgumentException for a start vertex that does not exist
    /// (lookups are case-insensitive, and results use the stored spelling). Edge weights are ignored here.
    /// </summary>
    public static class GraphTraversal
    {
        /// <summary>Breadth-first: which devices can the start reach, and in how few hops?</summary>
        public static TraversalResult Bfs(MeshGraph graph, string start)
        {
            start = graph.Resolve(start);

            var order = new List<string>();
            var hops = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [start] = 0 };
            var parent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [start] = null };
            var queue = new Queue<string>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                order.Add(current);

                foreach (var (neighbour, _) in graph.Neighbors(current))
                {
                    // A device is "seen" as soon as it is queued, so a hops entry doubles as the visited marker.
                    if (hops.ContainsKey(neighbour)) continue;

                    hops[neighbour] = hops[current] + 1;
                    parent[neighbour] = current;
                    queue.Enqueue(neighbour);
                }
            }

            return new TraversalResult(order, hops, parent);
        }

        /// <summary>Depth-first. Must be iterative: a long chain of devices must not overflow the stack.</summary>
        public static TraversalResult Dfs(MeshGraph graph, string start)
        {
            start = graph.Resolve(start);

            var order = new List<string>();
            var hops = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var parent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            // Each stack entry is a device plus the device that put it there. A device can be pushed several
            // times (once per neighbour that reaches it), so it is only visited the first time it is popped.
            var stack = new Stack<(string Vertex, string? From)>();
            stack.Push((start, null));

            while (stack.Count > 0)
            {
                var (current, from) = stack.Pop();
                if (hops.ContainsKey(current)) continue;

                hops[current] = from is null ? 0 : hops[from] + 1;
                parent[current] = from;
                order.Add(current);

                foreach (var (neighbour, _) in graph.Neighbors(current))
                {
                    if (!hops.ContainsKey(neighbour)) stack.Push((neighbour, current));
                }
            }

            return new TraversalResult(order, hops, parent);
        }

        /// <summary>The groups of devices that can reach each other. Every vertex is in exactly one group.</summary>
        public static List<List<string>> ConnectedComponents(MeshGraph graph)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var components = new List<List<string>>();

            foreach (var vertex in graph.Vertices.ToList())
            {
                if (seen.Contains(vertex)) continue;

                // Everything BFS can reach from an unvisited device is one group.
                var group = Bfs(graph, vertex).Order;
                seen.UnionWith(group);
                components.Add(group);
            }

            return components;
        }
    }
}
