namespace Smart_X.Shared
{
    /// <summary>
    /// Represents a node in a deployment hierarchy, which can be a facility, zone, subzone, or device. 
    /// Each node has a name, a level indicating its type, and a list of child nodes. 
    /// The class provides functionality to validate the node and its children.
    /// </summary>
    public class DeploymentNode
    {
        public string Name { get; }
        public Level Level { get; }

        public List<DeploymentNode> Children { get; } = new List<DeploymentNode>();

        public DeploymentNode(string name, Level level)
        {
            Name = name;
            Level = level;
        }

        private static readonly Level[] LevelOrder = { Level.Facility, Level.Zone, Level.SubZone, Level.Device };

        // Builds a Facility -> Zone -> SubZone -> Device chain from a "/"-delimited
        // location path, e.g. "Estate/Block A/Row 3". Extra segments past Device
        // all stay at Device level.
        public static DeploymentNode FromPath(string? path)
        {
            var segments = (path ?? string.Empty).Split('/', StringSplitOptions.TrimEntries);

            DeploymentNode? root = null;
            DeploymentNode? current = null;
            for (int i = 0; i < segments.Length; i++)
            {
                var level = LevelOrder[Math.Min(i, LevelOrder.Length - 1)];
                var node = new DeploymentNode(segments[i], level);
                if (root == null)
                {
                    root = node;
                }
                else
                {
                    current!.Children.Add(node);
                }
                current = node;
            }

            return root ?? new DeploymentNode(string.Empty, Level.Facility);
        }

        // ValidateNode method checks if the node has a valid name and recursively validates its child nodes. If any child node is invalid, the entire node is considered invalid.
        public static bool ValidateNode(DeploymentNode node)
        {
            bool isValid = false;
            // Check if the node has a valid name
            if (!string.IsNullOrWhiteSpace(node.Name))
            {
                isValid = true;
                // Recursively validate child nodes
                foreach (var child in node.Children)
                {
                    // If any child node is invalid, the entire node is considered invalid
                    if (!ValidateNode(child))
                    {
                        isValid = false;
                        break;
                    }
                }

            }
            return isValid;
        }
    }
}