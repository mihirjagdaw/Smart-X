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

        // ValidateNode method checks if the node has a valid name and recursively validates its child nodes. If any child node is invalid, the entire node is considered invalid.
        public static bool ValidateNode(DeploymentNode node)
        {
            bool isValid = false;
            // Check if the node has a valid name
            if (node.Name != null)
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