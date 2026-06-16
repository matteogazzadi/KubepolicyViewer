using k8s;
using k8s.Models;
using KubepolicyViewer.Models;

namespace KubepolicyViewer.Services;

public class KubernetesService
{
    private IKubernetes? _client;
    public string? InitializationError { get; private set; }

    public KubernetesService(ILogger<KubernetesService> logger)
    {
        try
        {
            var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
            _client = new Kubernetes(config);
            logger.LogInformation("Connected to Kubernetes cluster at {Host}", config.Host);
        }
        catch (Exception ex)
        {
            InitializationError = $"Failed to load kubeconfig: {ex.Message}";
            logger.LogError(ex, "Failed to initialize Kubernetes client");
        }
    }

    public async Task<List<NamespaceInfo>> GetNamespacesAsync()
    {
        EnsureClient();
        var result = await _client!.CoreV1.ListNamespaceAsync();
        return result.Items
            .Select(n => new NamespaceInfo(
                n.Metadata.Name,
                ToDict(n.Metadata.Labels)))
            .OrderBy(n => n.Name)
            .ToList();
    }

    public async Task<List<PodInfo>> GetPodsAsync(string namespaceName)
    {
        EnsureClient();
        var result = await _client!.CoreV1.ListNamespacedPodAsync(namespaceName);
        return result.Items
            .Select(p => new PodInfo(
                p.Metadata.Name,
                p.Metadata.NamespaceProperty ?? namespaceName,
                ToDict(p.Metadata.Labels),
                p.Status?.Phase ?? "Unknown"))
            .OrderBy(p => p.Name)
            .ToList();
    }

    public async Task<(List<NetworkPolicyInfo> Policies, PodInfo? Pod)> GetNetworkPoliciesForPodAsync(
        string namespaceName, string podName)
    {
        EnsureClient();

        var podTask = _client!.CoreV1.ReadNamespacedPodAsync(podName, namespaceName);
        var policiesTask = _client.NetworkingV1.ListNamespacedNetworkPolicyAsync(namespaceName);

        await Task.WhenAll(podTask, policiesTask);

        var pod = podTask.Result;
        var allPolicies = policiesTask.Result;
        var podLabels = ToDict(pod.Metadata.Labels);

        var podInfo = new PodInfo(
            pod.Metadata.Name,
            pod.Metadata.NamespaceProperty ?? namespaceName,
            podLabels,
            pod.Status?.Phase ?? "Unknown");

        var matching = allPolicies.Items
            .Where(p => LabelsMatchSelector(podLabels, p.Spec.PodSelector))
            .Select(MapPolicy)
            .ToList();

        return (matching, podInfo);
    }

    private NetworkPolicyInfo MapPolicy(V1NetworkPolicy p)
    {
        var info = new NetworkPolicyInfo
        {
            Name = p.Metadata.Name,
            Namespace = p.Metadata.NamespaceProperty ?? "",
            PodSelectorStr = SelectorToString(p.Spec.PodSelector),
            PolicyTypes = p.Spec.PolicyTypes?.ToList() ?? []
        };

        if (p.Spec.Ingress != null)
        {
            info.IngressRules = p.Spec.Ingress.Select(r => new IngressRuleInfo
            {
                From = r.FromProperty?.Select(MapPeer).ToList() ?? [],
                Ports = MapPorts(r.Ports)
            }).ToList();
        }

        if (p.Spec.Egress != null)
        {
            info.EgressRules = p.Spec.Egress.Select(r => new EgressRuleInfo
            {
                To = r.To?.Select(MapPeer).ToList() ?? [],
                Ports = MapPorts(r.Ports)
            }).ToList();
        }

        return info;
    }

    private PeerInfo MapPeer(V1NetworkPolicyPeer peer)
    {
        if (peer.IpBlock != null)
        {
            string? except = peer.IpBlock.Except != null
                ? string.Join(", ", peer.IpBlock.Except)
                : null;
            return new PeerInfo("ipBlock", null, null, peer.IpBlock.Cidr, except);
        }

        bool hasPodSel = HasSelectors(peer.PodSelector);
        bool hasNsSel = peer.NamespaceSelector != null;

        return new PeerInfo(
            (hasPodSel || hasNsSel) ? "pod" : "all",
            peer.NamespaceSelector != null ? SelectorToString(peer.NamespaceSelector) : null,
            peer.PodSelector != null ? SelectorToString(peer.PodSelector) : null,
            null, null);
    }

    private List<PortInfo> MapPorts(IList<V1NetworkPolicyPort>? ports)
    {
        if (ports == null || ports.Count == 0)
            return [new PortInfo("TCP", "any")];

        return ports.Select(p => new PortInfo(
            p.Protocol ?? "TCP",
            p.Port?.ToString() ?? "any"
        )).ToList();
    }

    private bool LabelsMatchSelector(Dictionary<string, string> labels, V1LabelSelector? selector)
    {
        if (selector == null) return true;

        bool hasMatchLabels = selector.MatchLabels != null && selector.MatchLabels.Count > 0;
        bool hasMatchExpressions = selector.MatchExpressions != null && selector.MatchExpressions.Count > 0;

        if (!hasMatchLabels && !hasMatchExpressions) return true;

        if (selector.MatchLabels != null)
        {
            foreach (var kv in selector.MatchLabels)
            {
                if (!labels.TryGetValue(kv.Key, out var val) || val != kv.Value)
                    return false;
            }
        }

        if (selector.MatchExpressions != null)
        {
            foreach (var expr in selector.MatchExpressions)
            {
                switch (expr.OperatorProperty)
                {
                    case "In":
                        if (!labels.TryGetValue(expr.Key, out var inVal)
                            || !(expr.Values ?? []).Contains(inVal))
                            return false;
                        break;
                    case "NotIn":
                        if (labels.TryGetValue(expr.Key, out var notInVal)
                            && (expr.Values ?? []).Contains(notInVal))
                            return false;
                        break;
                    case "Exists":
                        if (!labels.ContainsKey(expr.Key)) return false;
                        break;
                    case "DoesNotExist":
                        if (labels.ContainsKey(expr.Key)) return false;
                        break;
                }
            }
        }

        return true;
    }

    private bool HasSelectors(V1LabelSelector? sel) =>
        sel != null && ((sel.MatchLabels?.Count > 0) || (sel.MatchExpressions?.Count > 0));

    private string SelectorToString(V1LabelSelector? selector)
    {
        if (selector == null) return "*";

        var parts = new List<string>();

        if (selector.MatchLabels != null)
            parts.AddRange(selector.MatchLabels.Select(kv => $"{kv.Key}={kv.Value}"));

        if (selector.MatchExpressions != null)
            parts.AddRange(selector.MatchExpressions.Select(e =>
                $"{e.Key} {e.OperatorProperty.ToLower()} [{string.Join(",", e.Values ?? [])}]"));

        return parts.Count == 0 ? "*" : string.Join(", ", parts);
    }

    private static Dictionary<string, string> ToDict(IDictionary<string, string>? src)
        => src?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [];

    private void EnsureClient()
    {
        if (_client == null)
            throw new InvalidOperationException(InitializationError ?? "Kubernetes client not initialized");
    }
}
