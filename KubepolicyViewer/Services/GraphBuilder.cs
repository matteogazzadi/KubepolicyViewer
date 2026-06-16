using KubepolicyViewer.Models;

namespace KubepolicyViewer.Services;

public static class GraphBuilder
{
    public static GraphData Build(PodInfo pod, List<NetworkPolicyInfo> policies)
    {
        var nodes = new List<VisNode>();
        var edges = new List<VisEdge>();
        var nodeIds = new HashSet<string>();
        int edgeSeq = 0;

        string podNodeId = $"pod:{pod.Name}";
        nodes.Add(new VisNode
        {
            Id = podNodeId,
            Label = pod.Name,
            Group = "pod",
            Title = $"Pod: {pod.Name}\nNamespace: {pod.Namespace}\nLabels: {LabelsStr(pod.Labels)}"
        });
        nodeIds.Add(podNodeId);

        string EnsureNode(string id, string label, string group, string? title = null)
        {
            if (!nodeIds.Contains(id))
            {
                nodes.Add(new VisNode { Id = id, Label = label, Group = group, Title = title });
                nodeIds.Add(id);
            }
            return id;
        }

        void AddEdge(string from, string to, string label, string? color, string? title = null, bool dashes = false)
        {
            edges.Add(new VisEdge
            {
                Id = $"e{edgeSeq++}",
                From = from,
                To = to,
                Label = label,
                Color = color,
                Title = title,
                Dashes = dashes ? true : null
            });
        }

        bool hasIngressControl = policies.Any(p => p.PolicyTypes.Contains("Ingress"));
        bool hasEgressControl = policies.Any(p => p.PolicyTypes.Contains("Egress"));

        if (!hasIngressControl)
        {
            string id = EnsureNode("any_ingress_uncontrolled", "All ingress\n(unrestricted)", "any_ingress",
                "No NetworkPolicy controls ingress — all sources allowed by default");
            AddEdge(id, podNodeId, "any", "#ffd600");
        }

        if (!hasEgressControl)
        {
            string id = EnsureNode("any_egress_uncontrolled", "All egress\n(unrestricted)", "any_egress",
                "No NetworkPolicy controls egress — all destinations allowed by default");
            AddEdge(podNodeId, id, "any", "#ff9800");
        }

        foreach (var policy in policies)
        {
            foreach (var rule in policy.IngressRules)
            {
                string portLabel = PortsLabel(rule.Ports);

                if (rule.AllowsAll)
                {
                    string id = EnsureNode($"ingress_any:{policy.Name}",
                        $"Any source\n[{policy.Name}]", "any_ingress",
                        $"Policy: {policy.Name}\nAllow all ingress sources");
                    AddEdge(id, podNodeId, portLabel, "#ffd600",
                        $"Policy: {policy.Name}\nPorts: {portLabel}");
                }
                else
                {
                    foreach (var peer in rule.From)
                    {
                        // deduplicate peer nodes across policies — same peer label = same node
                        string id = EnsureNode($"ingress_src:{peer.DisplayLabel}",
                            peer.DisplayLabel, "ingress_source", peer.DisplayLabel);
                        AddEdge(id, podNodeId, portLabel, "#2e7d32",
                            $"Policy: {policy.Name}\nFrom: {peer.DisplayLabel}\nPorts: {portLabel}");
                    }
                }
            }

            if (policy.PolicyTypes.Contains("Ingress") && policy.IngressRules.Count == 0)
            {
                string id = EnsureNode($"ingress_blocked:{policy.Name}",
                    $"Blocked\n[{policy.Name}]", "blocked",
                    $"Policy: {policy.Name}\nAll ingress blocked — no rules defined");
                AddEdge(id, podNodeId, "blocked", "#c62828", dashes: true);
            }

            foreach (var rule in policy.EgressRules)
            {
                string portLabel = PortsLabel(rule.Ports);

                if (rule.AllowsAll)
                {
                    string id = EnsureNode($"egress_any:{policy.Name}",
                        $"Any dest\n[{policy.Name}]", "any_egress",
                        $"Policy: {policy.Name}\nAllow all egress destinations");
                    AddEdge(podNodeId, id, portLabel, "#ff9800",
                        $"Policy: {policy.Name}\nPorts: {portLabel}");
                }
                else
                {
                    foreach (var peer in rule.To)
                    {
                        // deduplicate peer nodes across policies — same peer label = same node
                        string id = EnsureNode($"egress_dst:{peer.DisplayLabel}",
                            peer.DisplayLabel, "egress_dest", peer.DisplayLabel);
                        AddEdge(podNodeId, id, portLabel, "#e65100",
                            $"Policy: {policy.Name}\nTo: {peer.DisplayLabel}\nPorts: {portLabel}");
                    }
                }
            }

            if (policy.PolicyTypes.Contains("Egress") && policy.EgressRules.Count == 0)
            {
                string id = EnsureNode($"egress_blocked:{policy.Name}",
                    $"Blocked\n[{policy.Name}]", "blocked",
                    $"Policy: {policy.Name}\nAll egress blocked — no rules defined");
                AddEdge(podNodeId, id, "blocked", "#c62828", dashes: true);
            }
        }

        return new GraphData { Nodes = nodes, Edges = edges };
    }

    private static string PortsLabel(List<PortInfo> ports) =>
        ports.Count == 0 ? "any" : string.Join(", ", ports.Select(p => p.Display));

    private static string LabelsStr(Dictionary<string, string> labels) =>
        labels.Count == 0 ? "(none)" : string.Join(", ", labels.Select(kv => $"{kv.Key}={kv.Value}"));
}
