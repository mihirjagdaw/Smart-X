namespace Smart_X.Api.Network
{
    /// <summary>
    /// Cheapest routes from one source. Only vertices the source can reach appear in these collections.
    ///  - Distance: the lowest total weight from the source. The source itself is 0.
    ///  - Parent: the previous vertex on that cheapest route. The source's parent is null.
    /// </summary>
    public record ShortestPaths(Dictionary<string, double> Distance, Dictionary<string, string?> Parent)
    {
        /// <summary>The cheapest route source..target, or null when the target cannot be reached.</summary>
        public List<string>? PathTo(string target) => TraversalResult.RouteTo(Parent, target);
    }

    public static class ShortestPath
    {
        /// <summary>
        /// Dijkstra's algorithm from one source to every reachable vertex, using a PriorityQueue.
        /// ArgumentException when the source does not exist. Weights are never negative (MeshGraph guarantees it).
        /// </summary>
        public static ShortestPaths Dijkstra(MeshGraph graph, string source)
        {
            source = graph.Resolve(source);

            var distance = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { [source] = 0 };
            var parent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [source] = null };

            // Always settle the closest unsettled device next. PriorityQueue cannot lower an entry's priority,
            // so a cheaper route simply queues the device again and the older, dearer entry is skipped later.
            var queue = new PriorityQueue<string, double>();
            queue.Enqueue(source, 0);

            while (queue.TryDequeue(out var current, out var cost))
            {
                if (cost > distance[current]) continue;

                foreach (var (neighbour, weight) in graph.Neighbors(current))
                {
                    var viaCurrent = cost + weight;
                    if (distance.TryGetValue(neighbour, out var best) && viaCurrent >= best) continue;

                    distance[neighbour] = viaCurrent;
                    parent[neighbour] = current;
                    queue.Enqueue(neighbour, viaCurrent);
                }
            }

            return new ShortestPaths(distance, parent);
        }
    }
}
