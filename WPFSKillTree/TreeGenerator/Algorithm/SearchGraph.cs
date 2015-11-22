using System.Collections.Generic;
using POESKillTree.SkillTreeFiles;

namespace POESKillTree.TreeGenerator.Algorithm
{
    /// <summary>
    /// A graph representing a simplified skill tree. 
    /// </summary>
    public class SearchGraph
    {
        public readonly Dictionary<SkillNode, GraphNode> NodeDict;

        public SearchGraph()
        {
            NodeDict = new Dictionary<SkillNode, GraphNode>();
        }

        /// <summary>
        ///  Adds a skill node to the graph. New nodes are automatically
        ///  connected to existing adjacent nodes.
        /// </summary>
        /// <param name="node">The skill node to be added.</param>
        /// <returns>The graph node that is added to the graph.</returns>
        public GraphNode AddNode(SkillNode node)
        {
            var graphNode = new GraphNode(node.Id);
            NodeDict.Add(node, graphNode);
            CheckLinks(node);
            return graphNode;
        }

        public GraphNode AddNodeId(ushort nodeId)
        {
            return AddNode(SkillTree.Skillnodes[nodeId]);
        }

        public GraphNode SetStartNodes(HashSet<ushort> startNodes)
        {
            var supernode = new GraphNode(startNodes);
            foreach (ushort nodeId in startNodes)
            {
                SkillNode node = SkillTree.Skillnodes[nodeId];
                NodeDict.Add(node, supernode);
                CheckLinks(node);
            }
            return supernode;
        }

        private void CheckLinks(SkillNode node)
        {
            if (!NodeDict.ContainsKey(node)) return;
            GraphNode currentNode = NodeDict[node];

            foreach (SkillNode neighbor in node.Neighbor)
            {
                if (NodeDict.ContainsKey(neighbor))
                {
                    GraphNode adjacentNode = NodeDict[neighbor];

                    if (adjacentNode == currentNode) continue;

                    adjacentNode.AddNeighbor(currentNode);
                    currentNode.AddNeighbor(adjacentNode);
                }
            }
        }
    }
}