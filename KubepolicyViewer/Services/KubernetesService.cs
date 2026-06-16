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
            .Where(p => PolicyMatcher.LabelsMatchSelector(podLabels, p.Spec.PodSelector))
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
            PodSelectorStr = PolicyMatcher.SelectorToString(p.Spec.PodSelector),
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

        bool hasPodSel = PolicyMatcher.HasSelectors(peer.PodSelector);
        bool hasNsSel = peer.NamespaceSelector != null;

        return new PeerInfo(
            (hasPodSel || hasNsSel) ? "pod" : "all",
            peer.NamespaceSelector != null ? PolicyMatcher.SelectorToString(peer.NamespaceSelector) : null,
            peer.PodSelector != null ? PolicyMatcher.SelectorToString(peer.PodSelector) : null,
            null, null);
    }

    private static List<PortInfo> MapPorts(IList<V1NetworkPolicyPort>? ports)
    {
        if (ports == null || ports.Count == 0)
            return [new PortInfo("TCP", "any")];

        return ports.Select(p => new PortInfo(
            p.Protocol ?? "TCP",
            p.Port?.ToString() ?? "any"
        )).ToList();
    }

    private static Dictionary<string, string> ToDict(IDictionary<string, string>? src)
        => src?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [];

    private void EnsureClient()
    {
        if (_client == null)
            throw new InvalidOperationException(InitializationError ?? "Kubernetes client not initialized");
    }
}
