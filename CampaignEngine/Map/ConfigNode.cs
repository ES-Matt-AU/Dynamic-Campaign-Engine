using System.Collections.Generic;

namespace CampaignEngine.Map
{
    /// <summary>
    /// Minimal reader for VTOL VR's ConfigNode text format (.vtm maps and .vts scenarios):
    /// a node name on its own line, then "{", then "key = value" lines and child nodes, then "}".
    /// </summary>
    public class ConfigNode
    {
        public string Name;
        public List<KeyValuePair<string, string>> Values = new List<KeyValuePair<string, string>>();
        public List<ConfigNode> Nodes = new List<ConfigNode>();

        public ConfigNode(string name)
        {
            Name = name;
        }

        public string GetValue(string key)
        {
            foreach (var pair in Values)
                if (pair.Key == key)
                    return pair.Value;
            return null;
        }

        public ConfigNode GetNode(string name)
        {
            foreach (var node in Nodes)
                if (node.Name == name)
                    return node;
            return null;
        }

        public IEnumerable<ConfigNode> GetNodes(string name)
        {
            foreach (var node in Nodes)
                if (node.Name == name)
                    yield return node;
        }

        public static ConfigNode Parse(string text)
        {
            var root = new ConfigNode("root");
            var stack = new Stack<ConfigNode>();
            stack.Push(root);
            string pendingName = null;

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (line == "{")
                {
                    var node = new ConfigNode(pendingName ?? "");
                    stack.Peek().Nodes.Add(node);
                    stack.Push(node);
                    pendingName = null;
                }
                else if (line == "}")
                {
                    if (stack.Count > 1)
                        stack.Pop();
                }
                else
                {
                    int eq = line.IndexOf('=');
                    if (eq >= 0)
                        stack.Peek().Values.Add(new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
                    else
                        pendingName = line;
                }
            }

            return root;
        }
    }
}
