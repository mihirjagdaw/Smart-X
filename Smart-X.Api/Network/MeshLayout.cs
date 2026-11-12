using Smart_X.Api.Processing;

namespace Smart_X.Api.Network
{
    /// <summary>
    /// Where each device sits on the estate map, worked out from its location path so that nobody has to enter
    /// coordinates. Devices in the same zone (the first two parts of the location) form a block laid out on a
    /// grid, blocks are tiled across the map, and the gateway sits at the left edge. The result depends only on
    /// the devices themselves, so the same devices always get the same positions.
    /// </summary>
    public static class MeshLayout
    {
        public const string GatewayId = "GATEWAY";

        private const double Margin = 100;
        private const double BlockGap = 55;
        private const double GatewayOffset = 60;

        public static string[] Segments(string? location) =>
            (location ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>
        /// The zone a device belongs to: the first two parts of its location path, so "Estate/Block A/Row 3"
        /// is in "Estate/Block A". A one-part location is its own zone.
        /// </summary>
        public static string Zone(string? location)
        {
            var parts = Segments(location);
            return parts.Length == 0 ? "Unassigned" : string.Join('/', parts.Take(2));
        }

        /// <summary>
        /// True when the location lies inside the subnet, comparing whole path parts: "Estate/Block A" covers
        /// "Estate/Block A/Row 3" but not "Estate/Block AB".
        /// </summary>
        public static bool InSubnet(string? location, string subnet)
        {
            var want = Segments(subnet);
            var have = Segments(location);
            if (want.Length == 0) return true;
            if (have.Length < want.Length) return false;

            for (var i = 0; i < want.Length; i++)
            {
                if (!string.Equals(have[i], want[i], StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        public record Placement(Dictionary<string, (double X, double Y)> Positions, (double X, double Y) Gateway);

        public static Placement Place(IEnumerable<DeviceState> devices, MeshOptions options)
        {
            var zones = devices
                .GroupBy(d => Zone(d.Location), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Members: g.OrderBy(d => d.Id).ThenBy(d => d.Mac, StringComparer.OrdinalIgnoreCase).ToList()))
                .ToList();

            var positions = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
            if (zones.Count == 0) return new Placement(positions, (Margin, Margin));

            // Every zone gets a block as large as the biggest one, so the blocks tile evenly.
            var spacing = options.SpacingMetres;
            var columnsOf = zones.Select(z => (int)Math.Ceiling(Math.Sqrt(z.Members.Count))).ToList();
            var blockColumns = columnsOf.Max();
            var blockRows = zones.Max(z => (int)Math.Ceiling(z.Members.Count / Math.Ceiling(Math.Sqrt(z.Members.Count))));
            double blockWidth = (blockColumns - 1) * spacing, blockHeight = (blockRows - 1) * spacing;
            var tilesAcross = (int)Math.Ceiling(Math.Sqrt(zones.Count));

            for (var z = 0; z < zones.Count; z++)
            {
                var columns = columnsOf[z];
                var rows = (int)Math.Ceiling(zones[z].Members.Count / (double)columns);
                var originX = Margin + z % tilesAcross * (blockWidth + BlockGap);
                var originY = Margin + z / tilesAcross * (blockHeight + BlockGap);

                // Centre this zone's grid inside its block.
                var offsetX = (blockWidth - (columns - 1) * spacing) / 2;
                var offsetY = (blockHeight - (rows - 1) * spacing) / 2;

                for (var i = 0; i < zones[z].Members.Count; i++)
                {
                    var mac = zones[z].Members[i].Mac;
                    positions[mac] = (
                        originX + offsetX + i % columns * spacing + Jitter(mac, 0) * spacing * 0.15,
                        originY + offsetY + i / columns * spacing + Jitter(mac, 16) * spacing * 0.15);
                }
            }

            return new Placement(positions, (Margin - GatewayOffset, Margin + blockHeight / 2));
        }

        // A repeatable value between -1 and 1 for a device, so the grid looks a little organic without ever
        // changing between runs. (string.GetHashCode is randomised per process, so it cannot be used here.)
        private static double Jitter(string mac, int shift)
        {
            var hash = 2166136261u;
            foreach (var c in mac.ToLowerInvariant()) hash = (hash ^ c) * 16777619u;
            return ((hash >> shift) & 0xFFFF) / 65535.0 * 2 - 1;
        }
    }
}
