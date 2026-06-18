using System.Text.Json.Serialization;

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
        "all"     => "All Sources",
        "ipBlock" => $"IP: {IpBlock}" + (Except != null ? $"\nexcept {Except}" : ""),
        "namespace" => $"NS: {NamespaceSelector ?? "*"}",
        "pod"     => BuildPodLabel(),
        _         => Kind
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

public record PolicySummary(
    string PolicyName,
    string Namespace,
    string PodSelectorStr,
    List<string> PolicyTypes,
    int AffectedPodCount
);

public enum ConnectivityStatus { Unrestricted, Allowed, Blocked }

public class ConnectivityResult
{
    public ConnectivityStatus IngressStatus { get; set; }
    public ConnectivityStatus EgressStatus { get; set; }
    public bool CanConnect =>
        IngressStatus != ConnectivityStatus.Blocked &&
        EgressStatus  != ConnectivityStatus.Blocked;
    public List<string> IngressBlockingPolicies { get; set; } = [];
    public List<string> EgressBlockingPolicies  { get; set; } = [];
    public List<string> IngressAllowingPolicies { get; set; } = [];
    public List<string> EgressAllowingPolicies  { get; set; } = [];
}

// ── Graph models — properties MUST be camelCase for vis-network ──────────────

public class GraphData
{
    [JsonPropertyName("nodes")]
    public List<VisNode> Nodes { get; set; } = [];

    [JsonPropertyName("edges")]
    public List<VisEdge> Edges { get; set; } = [];
}

public class VisNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }
}

public class VisEdge
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("from")]
    public string From { get; set; } = "";

    [JsonPropertyName("to")]
    public string To { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonPropertyName("color")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Color { get; set; }

    [JsonPropertyName("dashes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Dashes { get; set; }
}
