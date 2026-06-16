namespace KubepolicyViewer.Models;

public record PodInfo(string Name, string Namespace, Dictionary<string, string> Labels, string Phase);

public record NamespaceInfo(string Name, Dictionary<string, string> Labels);

public class NetworkPolicyInfo
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string PodSelectorStr { get; set; } = "";
    public List<string> PolicyTypes { get; set; } = [];
    public List<IngressRuleInfo> IngressRules { get; set; } = [];
    public List<EgressRuleInfo> EgressRules { get; set; } = [];
}

public class IngressRuleInfo
{
    public List<PeerInfo> From { get; set; } = [];
    public List<PortInfo> Ports { get; set; } = [];
    public bool AllowsAll => From.Count == 0;
}

public class EgressRuleInfo
{
    public List<PeerInfo> To { get; set; } = [];
    public List<PortInfo> Ports { get; set; } = [];
    public bool AllowsAll => To.Count == 0;
}

public record PeerInfo(
    string Kind,
    string? NamespaceSelector,
    string? PodSelector,
    string? IpBlock,
    string? Except
)
{
    public string DisplayLabel => Kind switch
    {
        "all" => "All Sources",
        "ipBlock" => $"IP: {IpBlock}" + (Except != null ? $"\nexcept {Except}" : ""),
        "namespace" => $"NS: {NamespaceSelector ?? "*"}",
        "pod" => BuildPodLabel(),
        _ => Kind
    };

    private string BuildPodLabel()
    {
        var parts = new List<string>();
        if (NamespaceSelector != null) parts.Add($"NS: {NamespaceSelector}");
        if (PodSelector != null) parts.Add($"Pod: {PodSelector}");
        return parts.Count > 0 ? string.Join("\n", parts) : "Any Pod";
    }
}

public record PortInfo(string Protocol, string Port)
{
    public string Display => Port == "any" ? "any" : $"{Port}/{Protocol}";
}

public class GraphData
{
    public List<VisNode> Nodes { get; set; } = [];
    public List<VisEdge> Edges { get; set; } = [];
}

public class VisNode
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Group { get; set; } = "";
    public string? Title { get; set; }
}

public class VisEdge
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Title { get; set; }
    public string? Color { get; set; }
    public bool? Dashes { get; set; }
}
