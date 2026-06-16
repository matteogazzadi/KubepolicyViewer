using KubepolicyViewer.Models;
using KubepolicyViewer.Services;

namespace KubepolicyViewer.Tests;

public class GraphBuilderTests
{
    private static PodInfo MakePod(string name = "my-pod", Dictionary<string, string>? labels = null) =>
        new(name, "default", labels ?? [], "Running");

    private static NetworkPolicyInfo MakePolicy(
        string name,
        string[] types,
        List<IngressRuleInfo>? ingress = null,
        List<EgressRuleInfo>? egress = null) =>
        new()
        {
            Name = name,
            Namespace = "default",
            PodSelectorStr = "*",
            PolicyTypes = [.. types],
            IngressRules = ingress ?? [],
            EgressRules = egress ?? []
        };

    // ── Pod node ─────────────────────────────────────────────────────────────

    [Fact]
    public void PodNodeAlwaysPresent()
    {
        var graph = GraphBuilder.Build(MakePod(), []);
        Assert.Contains(graph.Nodes, n => n.Group == "pod");
    }

    [Fact]
    public void PodNodeIdContainsPodName()
    {
        var graph = GraphBuilder.Build(MakePod("web-server"), []);
        Assert.Contains(graph.Nodes, n => n.Id.Contains("web-server") && n.Group == "pod");
    }

    // ── No-policy defaults ───────────────────────────────────────────────────

    [Fact]
    public void NoPolicies_AddsUnrestrictedIngressNode()
    {
        var graph = GraphBuilder.Build(MakePod(), []);
        Assert.Contains(graph.Nodes, n => n.Group == "any_ingress");
    }

    [Fact]
    public void NoPolicies_AddsUnrestrictedEgressNode()
    {
        var graph = GraphBuilder.Build(MakePod(), []);
        Assert.Contains(graph.Nodes, n => n.Group == "any_egress");
    }

    [Fact]
    public void NoPolicies_HasTwoEdges_InAndOut()
    {
        var graph = GraphBuilder.Build(MakePod(), []);
        Assert.Equal(2, graph.Edges.Count);
    }

    [Fact]
    public void IngressPolicyPresent_NoUnrestrictedIngressNode()
    {
        var policy = MakePolicy("deny-all", ["Ingress"]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        Assert.DoesNotContain(graph.Nodes, n => n.Id == "any_ingress_uncontrolled");
    }

    [Fact]
    public void EgressPolicyPresent_NoUnrestrictedEgressNode()
    {
        var policy = MakePolicy("deny-egress", ["Egress"]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        Assert.DoesNotContain(graph.Nodes, n => n.Id == "any_egress_uncontrolled");
    }

    // ── Blocked directions ───────────────────────────────────────────────────

    [Fact]
    public void IngressTypeWithNoRules_AddsBlockedNode()
    {
        var policy = MakePolicy("deny-all-ingress", ["Ingress"]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        Assert.Contains(graph.Nodes, n => n.Group == "blocked");
    }

    [Fact]
    public void IngressTypeWithNoRules_DashedEdgeTowardPod()
    {
        var policy = MakePolicy("deny-all-ingress", ["Ingress"]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        var podId = graph.Nodes.First(n => n.Group == "pod").Id;
        Assert.Contains(graph.Edges, e => e.To == podId && e.Dashes == true);
    }

    [Fact]
    public void EgressTypeWithNoRules_AddsBlockedNode()
    {
        var policy = MakePolicy("deny-all-egress", ["Egress"]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        Assert.Contains(graph.Nodes, n => n.Group == "blocked");
    }

    // ── Ingress allow-all rule ───────────────────────────────────────────────

    [Fact]
    public void IngressRule_EmptyFrom_AddsAnyIngressNode()
    {
        var policy = MakePolicy("allow-all-ingress", ["Ingress"],
            ingress: [new IngressRuleInfo { From = [], Ports = [new PortInfo("TCP", "80")] }]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        Assert.Contains(graph.Nodes, n => n.Group == "any_ingress");
    }

    // ── Ingress with specific peers ──────────────────────────────────────────

    [Fact]
    public void IngressRule_WithPeer_AddsIngressSourceNode()
    {
        var peer = new PeerInfo("pod", "env=prod", "app=web", null, null);
        var policy = MakePolicy("allow-from-web", ["Ingress"],
            ingress: [new IngressRuleInfo { From = [peer], Ports = [new PortInfo("TCP", "8080")] }]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        Assert.Contains(graph.Nodes, n => n.Group == "ingress_source");
    }

    [Fact]
    public void IngressRule_WithPeer_EdgePointsToPod()
    {
        var peer = new PeerInfo("pod", null, "app=web", null, null);
        var policy = MakePolicy("p1", ["Ingress"],
            ingress: [new IngressRuleInfo { From = [peer], Ports = [new PortInfo("TCP", "80")] }]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        var podId = graph.Nodes.First(n => n.Group == "pod").Id;
        Assert.Contains(graph.Edges, e => e.To == podId && !string.IsNullOrEmpty(e.From));
    }

    // ── Egress with specific peers ───────────────────────────────────────────

    [Fact]
    public void EgressRule_WithPeer_AddsEgressDestNode()
    {
        var peer = new PeerInfo("ipBlock", null, null, "10.0.0.0/8", null);
        var policy = MakePolicy("allow-to-internal", ["Egress"],
            egress: [new EgressRuleInfo { To = [peer], Ports = [new PortInfo("TCP", "443")] }]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        Assert.Contains(graph.Nodes, n => n.Group == "egress_dest");
    }

    [Fact]
    public void EgressRule_WithPeer_EdgeLeavesFromPod()
    {
        var peer = new PeerInfo("pod", null, "app=db", null, null);
        var policy = MakePolicy("p1", ["Egress"],
            egress: [new EgressRuleInfo { To = [peer], Ports = [new PortInfo("TCP", "5432")] }]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        var podId = graph.Nodes.First(n => n.Group == "pod").Id;
        Assert.Contains(graph.Edges, e => e.From == podId && !string.IsNullOrEmpty(e.To));
    }

    // ── Deduplication ────────────────────────────────────────────────────────

    [Fact]
    public void TwoPolicies_SamePeer_NodeNotDuplicated()
    {
        var peer = new PeerInfo("pod", null, "app=web", null, null);
        var rule = new IngressRuleInfo { From = [peer], Ports = [new PortInfo("TCP", "80")] };
        var p1 = MakePolicy("p1", ["Ingress"], ingress: [rule]);
        var p2 = MakePolicy("p2", ["Ingress"], ingress: [rule]);
        var graph = GraphBuilder.Build(MakePod(), [p1, p2]);
        var ingressNodes = graph.Nodes.Where(n => n.Group == "ingress_source").ToList();
        Assert.Equal(1, ingressNodes.Count);
    }

    // ── Edge uniqueness ───────────────────────────────────────────────────────

    [Fact]
    public void AllEdgeIds_AreUnique()
    {
        var peer = new PeerInfo("pod", null, "app=web", null, null);
        var policy = MakePolicy("p1", ["Ingress", "Egress"],
            ingress: [new IngressRuleInfo { From = [peer], Ports = [new PortInfo("TCP", "80")] }],
            egress: [new EgressRuleInfo { To = [peer], Ports = [new PortInfo("TCP", "443")] }]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        var ids = graph.Edges.Select(e => e.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }

    // ── Port label ───────────────────────────────────────────────────────────

    [Fact]
    public void EdgeLabel_ContainsPort()
    {
        var peer = new PeerInfo("pod", null, "app=web", null, null);
        var policy = MakePolicy("p1", ["Ingress"],
            ingress: [new IngressRuleInfo { From = [peer], Ports = [new PortInfo("TCP", "8080")] }]);
        var graph = GraphBuilder.Build(MakePod(), [policy]);
        var podId = graph.Nodes.First(n => n.Group == "pod").Id;
        var edge = graph.Edges.First(e => e.To == podId);
        Assert.Contains("8080", edge.Label);
    }
}
