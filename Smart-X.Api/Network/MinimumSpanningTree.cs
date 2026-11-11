namespace Smart_X.Api.Network
{
    /// <summary>A spanning tree (or forest): the chosen edges and their combined weight.</summary>
    public record MstResult(List<MeshEdge> Edges, double TotalWeight);

    /// <summary>
    /// Minimum spanning tree algorithms. When several trees tie for the lowest weight, either is correct:
    /// the tests compare total weight, not the exact edges.
    /// </summary>
    public static class MinimumSpanningTree
    {
        /// <summary>
        /// Prim's algorithm grown from root, using a PriorityQueue as the heap. It spans only the root's
        /// connected component. ArgumentException when the root does not exist.
        /// </summary>
        public static MstResult Prim(MeshGraph graph, string root)
        {
            root = graph.Resolve(root);

            var inTree = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            var edges = new List<MeshEdge>();
            double total = 0;

            // The heap always hands back the cheapest link that leaves the tree built so far.
            var frontier = new PriorityQueue<MeshEdge, double>();
            void AddLinksFrom(string vertex)
            {
                foreach (var (neighbour, weight) in graph.Neighbors(vertex))
                {
                    if (!inTree.Contains(neighbour)) frontier.Enqueue(new MeshEdge(vertex, neighbour, weight), weight);
                }
            }

            AddLinksFrom(root);
            while (frontier.TryDequeue(out var edge, out _))
            {
                // The far end may have joined the tree through a cheaper link since this one was queued.
                if (!inTree.Add(edge.B)) continue;

                edges.Add(edge);
                total += edge.Weight;
                AddLinksFrom(edge.B);
            }

            return new MstResult(edges, total);
        }

        /// <summary>
        /// Kruskal's algorithm with union-find. A graph that is not connected gets a minimum spanning forest
        /// (one tree per component), so it has VertexCount - components edges.
        /// </summary>
        public static MstResult Kruskal(MeshGraph graph)
        {
            // Union-find over numbered vertices: find() says which group a device is in, union merges two groups.
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var vertex in graph.Vertices) index[vertex] = index.Count;

            var group = Enumerable.Range(0, index.Count).ToArray();
            var rank = new int[index.Count];

            int Find(int x)
            {
                while (group[x] != x)
                {
                    group[x] = group[group[x]];
                    x = group[x];
                }
                return x;
            }

            var edges = new List<MeshEdge>();
            double total = 0;

            // Cheapest first; an edge is kept only if it joins two groups that were separate, so it can never close a loop.
            foreach (var edge in graph.Edges.OrderBy(e => e.Weight))
            {
                int a = Find(index[edge.A]), b = Find(index[edge.B]);
                if (a == b) continue;

                if (rank[a] < rank[b]) (a, b) = (b, a);
                group[b] = a;
                if (rank[a] == rank[b]) rank[a]++;

                edges.Add(edge);
                total += edge.Weight;
            }

            return new MstResult(edges, total);
        }
    }
}
