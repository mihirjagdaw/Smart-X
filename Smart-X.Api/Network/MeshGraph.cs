namespace Smart_X.Api.Network
{
    /// <summary>One undirected radio link between two devices. A and B are interchangeable.</summary>
    public readonly record struct MeshEdge(string A, string B, double Weight);

    /// <summary>
    /// The mesh as an undirected, weighted graph: vertices are devices (MAC address), edges are radio links,
    /// and a weight is the power cost of one hop. Stored as an adjacency list: each device keeps a small
    /// dictionary of its neighbours and what a hop to each costs.
    ///
    /// Rules (test-part3-graph.cs enforces them):
    ///  - Vertex ids are case-insensitive. The first spelling added is the one reported back.
    ///  - A weight must be finite and >= 0, otherwise ArgumentOutOfRangeException. Zero is allowed.
    ///  - A self-loop, or an edge to a vertex that does not exist, throws ArgumentException.
    /// </summary>
    public class MeshGraph
    {
        private sealed class Vertex
        {
            public Vertex(string id) => Id = id;

            public string Id { get; }

            // Neighbour id (always the stored spelling) -> cost of the hop to it.
            public Dictionary<string, double> Links { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Dictionary<string, Vertex> _vertices = new(StringComparer.OrdinalIgnoreCase);
        private int _edgeCount;

        public int VertexCount => _vertices.Count;

        public int EdgeCount => _edgeCount;

        public IEnumerable<string> Vertices => _vertices.Values.Select(v => v.Id);

        /// <summary>Every undirected edge exactly once, in any order and with A/B in either order.</summary>
        public IEnumerable<MeshEdge> Edges
        {
            get
            {
                foreach (var vertex in _vertices.Values)
                {
                    foreach (var (neighbour, weight) in vertex.Links)
                    {
                        // Each edge is stored at both ends. Report it from the end whose id sorts first, so it appears once.
                        if (string.Compare(vertex.Id, neighbour, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            yield return new MeshEdge(vertex.Id, neighbour, weight);
                        }
                    }
                }
            }
        }

        /// <summary>False when the vertex was already there.</summary>
        public bool AddVertex(string id) => _vertices.TryAdd(id, new Vertex(id));

        public bool HasVertex(string id) => _vertices.ContainsKey(id);

        /// <summary>The spelling this graph stores for the id, or ArgumentException when there is no such vertex.</summary>
        public string Resolve(string id) => Find(id).Id;

        /// <summary>False when there was no such vertex. Also removes every edge touching it.</summary>
        public bool RemoveVertex(string id)
        {
            if (!_vertices.TryGetValue(id, out var vertex)) return false;

            foreach (var neighbour in vertex.Links.Keys)
            {
                _vertices[neighbour].Links.Remove(vertex.Id);
            }
            _edgeCount -= vertex.Links.Count;
            _vertices.Remove(id);
            return true;
        }

        /// <summary>True when the edge is new. When it already exists its weight is replaced and false is returned.</summary>
        public bool AddEdge(string a, string b, double weight)
        {
            if (double.IsNaN(weight) || double.IsInfinity(weight) || weight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "A hop cost must be finite and not negative.");
            }

            var from = Find(a);
            var to = Find(b);
            if (ReferenceEquals(from, to)) throw new ArgumentException("A device cannot link to itself.");

            var isNew = !from.Links.ContainsKey(to.Id);
            from.Links[to.Id] = weight;
            to.Links[from.Id] = weight;
            if (isNew) _edgeCount++;
            return isNew;
        }

        /// <summary>False when there was no such edge.</summary>
        public bool RemoveEdge(string a, string b)
        {
            if (!_vertices.TryGetValue(a, out var from) || !_vertices.TryGetValue(b, out var to)) return false;
            if (!from.Links.Remove(to.Id)) return false;

            to.Links.Remove(from.Id);
            _edgeCount--;
            return true;
        }

        public bool HasEdge(string a, string b) => _vertices.TryGetValue(a, out var from) && from.Links.ContainsKey(b);

        /// <summary>ArgumentException when the vertex does not exist.</summary>
        public IEnumerable<(string Neighbor, double Weight)> Neighbors(string id)
        {
            // A copy, so a caller can change the graph while looping over the result.
            return Find(id).Links.Select(link => (link.Key, link.Value)).ToList();
        }

        /// <summary>
        /// An independent copy of the graph without the given vertices (and their edges). Ids that are not in
        /// the graph are ignored. This is how devices that are down drop out of the mesh.
        /// </summary>
        public MeshGraph Without(IEnumerable<string> removed)
        {
            var drop = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
            var copy = new MeshGraph();

            foreach (var id in Vertices.Where(v => !drop.Contains(v))) copy.AddVertex(id);
            foreach (var edge in Edges.Where(e => !drop.Contains(e.A) && !drop.Contains(e.B))) copy.AddEdge(edge.A, edge.B, edge.Weight);

            return copy;
        }

        private Vertex Find(string id) =>
            _vertices.TryGetValue(id, out var vertex) ? vertex : throw new ArgumentException($"Unknown vertex '{id}'.", nameof(id));
    }
}
