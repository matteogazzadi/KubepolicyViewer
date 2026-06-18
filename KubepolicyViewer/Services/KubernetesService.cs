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
            .Select(n => new NamespaceInfo(n.Metadata.Name, ToDict(n.Metadata.Labels)))
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

    /// <summary>
    /// Returns policies that SELECT the pod (same namespace) plus policies in OTHER
    /// namespaces whose FROM/TO namespaceSelector references the pod's namespace.
    /// </summary>
    public async Task<(List<NetworkPolicyInfo> Policies, List<NetworkPolicyInfo> CrossNsPolicies, PodInfo? Pod)>
        GetNetworkPoliciesForPodAsync(string namespaceName, string podName)
    {
        EnsureClient();

        var podTask = _client!.CoreV1.ReadNamespacedPodAsync(podName, namespaceName);
        var policiesTask = _client.NetworkingV1.ListNamespacedNetworkPolicyAsync(namespaceName);
        var nsTask = _client.CoreV1.ReadNamespaceAsync(namespaceName);

        await Task.WhenAll(podTask, policiesTask, nsTask);

        var pod = podTask.Result;
        var allPolicies = policiesTask.Result;
        var podNs = nsTask.Result;

        var podLabels = ToDict(pod.Metadata.Labels);
        var nsLabels = ToDict(podNs.Metadata.Labels);

        var podInfo = new PodInfo(
            pod.Metadata.Name,
            pod.Metadata.NamespaceProperty ?? namespaceName,
            podLabels,
            pod.Status?.Phase ?? "Unknown");

        var matching = allPolicies.Items
            .Where(p => PolicyMatcher.LabelsMatchSelector(podLabels, p.Spec.PodSelector))
            .Select(MapPolicy)
            .ToList();

        var crossNs = await GetCrossNamespacePoliciesAsync(namespaceName, nsLabels);

        return (matching, crossNs, podInfo);
    }

    // ── Cross-namespace: policies in other namespaces that reference this one ───

    private async Task<List<NetworkPolicyInfo>> GetCrossNamespacePoliciesAsync(
        string podNamespace, Dictionary<string, string> podNsLabels)
    {
        var allNsResult = await _client!.CoreV1.ListNamespaceAsync();
        var otherNs = allNsResult.Items
            .Where(n => n.Metadata.Name != podNamespace)
            .ToList();

        if (otherNs.Count == 0) return [];

        var policyTasks = otherNs
            .Select(n => _client.NetworkingV1.ListNamespacedNetworkPolicyAsync(n.Metadata.Name))
            .ToArray();

        var allLists = await Task.WhenAll(policyTasks);

        var result = new List<NetworkPolicyInfo>();
        foreach (var (_, list) in otherNs.Zip(allLists))
            foreach (var policy in list.Items)
                if (ReferencesNamespace(policy, podNsLabels))
                    result.Add(MapPolicy(policy));

        return result;
    }

    private static bool ReferencesNamespace(V1NetworkPolicy policy, Dictionary<string, string> nsLabels)
    {
        if (policy.Spec.Ingress != null)
            foreach (var rule in policy.Spec.Ingress)
                if (rule.FromProperty != null)
                    foreach (var peer in rule.FromProperty)
                        if (peer.NamespaceSelector != null &&
                            PolicyMatcher.LabelsMatchSelector(nsLabels, peer.NamespaceSelector))
                            return true;

        if (policy.Spec.Egress != null)
            foreach (var rule in policy.Spec.Egress)
                if (rule.To != null)
                    foreach (var peer in rule.To)
                        if (peer.NamespaceSelector != null &&
                            PolicyMatcher.LabelsMatchSelector(nsLabels, peer.NamespaceSelector))
                            return true;

        return false;
    }

    // ── Policy overview ──────────────────────────────────────────────────────

    public async Task<List<PolicySummary>> GetAllPoliciesWithPodCountAsync()
    {
        EnsureClient();
        var nsResult = await _client!.CoreV1.ListNamespaceAsync();

        var tasks = nsResult.Items.Select(async ns =>
        {
            var nsName = ns.Metadata.Name;
            var policiesTask = _client.NetworkingV1.ListNamespacedNetworkPolicyAsync(nsName);
            var podsTask     = _client.CoreV1.ListNamespacedPodAsync(nsName);
            await Task.WhenAll(policiesTask, podsTask);

            var podLabelsList = podsTask.Result.Items
                .Select(p => ToDict(p.Metadata.Labels))
                .ToList();

            return policiesTask.Result.Items.Select(p =>
            {
                var count = podLabelsList.Count(podLabels =>
                    PolicyMatcher.LabelsMatchSelector(podLabels, p.Spec.PodSelector));
                return new PolicySummary(
                    p.Metadata.Name,
                    nsName,
                    PolicyMatcher.SelectorToString(p.Spec.PodSelector),
                    p.Spec.PolicyTypes?.ToList() ?? [],
                    count);
            });
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        return results
            .SelectMany(x => x)
            .OrderBy(p => p.Namespace)
            .ThenBy(p => p.PolicyName)
            .ToList();
    }

    // ── Connectivity test ─────────────────────────────────────────────────────

    public async Task<ConnectivityResult> TestConnectivityAsync(
        string srcNamespace, string srcPodName,
        string dstNamespace, string dstPodName)
    {
        EnsureClient();

        var srcPodTask      = _client!.CoreV1.ReadNamespacedPodAsync(srcPodName, srcNamespace);
        var dstPodTask      = _client.CoreV1.ReadNamespacedPodAsync(dstPodName, dstNamespace);
        var srcNsTask       = _client.CoreV1.ReadNamespaceAsync(srcNamespace);
        var dstNsTask       = _client.CoreV1.ReadNamespaceAsync(dstNamespace);
        var srcPoliciesTask = _client.NetworkingV1.ListNamespacedNetworkPolicyAsync(srcNamespace);
        var dstPoliciesTask = _client.NetworkingV1.ListNamespacedNetworkPolicyAsync(dstNamespace);

        await Task.WhenAll(srcPodTask, dstPodTask, srcNsTask, dstNsTask, srcPoliciesTask, dstPoliciesTask);

        var srcPodLabels = ToDict(srcPodTask.Result.Metadata.Labels);
        var dstPodLabels = ToDict(dstPodTask.Result.Metadata.Labels);
        var srcNsLabels  = ToDict(srcNsTask.Result.Metadata.Labels);
        var dstNsLabels  = ToDict(dstNsTask.Result.Metadata.Labels);

        var result = new ConnectivityResult();

        // ── INGRESS check on dstPod ──────────────────────────────────────────
        var dstIngressPolicies = dstPoliciesTask.Result.Items
            .Where(p => p.Spec.PolicyTypes?.Contains("Ingress") == true &&
                        PolicyMatcher.LabelsMatchSelector(dstPodLabels, p.Spec.PodSelector))
            .ToList();

        if (dstIngressPolicies.Count == 0)
        {
            result.IngressStatus = ConnectivityStatus.Unrestricted;
        }
        else
        {
            bool ingressAllowed = false;
            foreach (var policy in dstIngressPolicies)
            {
                if (policy.Spec.Ingress == null || policy.Spec.Ingress.Count == 0)
                {
                    result.IngressBlockingPolicies.Add($"{policy.Metadata.NamespaceProperty}/{policy.Metadata.Name}");
                    continue;
                }

                bool policyAllows = policy.Spec.Ingress.Any(rule =>
                    rule.FromProperty == null || rule.FromProperty.Count == 0 ||
                    rule.FromProperty.Any(peer => PolicyMatcher.PeerMatchesSource(
                        peer, srcPodLabels, srcNsLabels, srcNamespace, dstNamespace)));

                if (policyAllows)
                {
                    ingressAllowed = true;
                    result.IngressAllowingPolicies.Add($"{policy.Metadata.NamespaceProperty}/{policy.Metadata.Name}");
                }
                else
                {
                    result.IngressBlockingPolicies.Add($"{policy.Metadata.NamespaceProperty}/{policy.Metadata.Name}");
                }
            }
            result.IngressStatus = ingressAllowed ? ConnectivityStatus.Allowed : ConnectivityStatus.Blocked;
        }

        // ── EGRESS check on srcPod ───────────────────────────────────────────
        var srcEgressPolicies = srcPoliciesTask.Result.Items
            .Where(p => p.Spec.PolicyTypes?.Contains("Egress") == true &&
                        PolicyMatcher.LabelsMatchSelector(srcPodLabels, p.Spec.PodSelector))
            .ToList();

        if (srcEgressPolicies.Count == 0)
        {
            result.EgressStatus = ConnectivityStatus.Unrestricted;
        }
        else
        {
            bool egressAllowed = false;
            foreach (var policy in srcEgressPolicies)
            {
                if (policy.Spec.Egress == null || policy.Spec.Egress.Count == 0)
                {
                    result.EgressBlockingPolicies.Add($"{policy.Metadata.NamespaceProperty}/{policy.Metadata.Name}");
                    continue;
                }

                bool policyAllows = policy.Spec.Egress.Any(rule =>
                    rule.To == null || rule.To.Count == 0 ||
                    rule.To.Any(peer => PolicyMatcher.PeerMatchesDestination(
                        peer, dstPodLabels, dstNsLabels, dstNamespace, srcNamespace)));

                if (policyAllows)
                {
                    egressAllowed = true;
                    result.EgressAllowingPolicies.Add($"{policy.Metadata.NamespaceProperty}/{policy.Metadata.Name}");
                }
                else
                {
                    result.EgressBlockingPolicies.Add($"{policy.Metadata.NamespaceProperty}/{policy.Metadata.Name}");
                }
            }
            result.EgressStatus = egressAllowed ? ConnectivityStatus.Allowed : ConnectivityStatus.Blocked;
        }

        return result;
    }

    // ── Mapping helpers ──────────────────────────────────────────────────────

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
            info.IngressRules = p.Spec.Ingress.Select(r => new IngressRuleInfo
            {
                From = r.FromProperty?.Select(MapPeer).ToList() ?? [],
                Ports = MapPorts(r.Ports)
            }).ToList();

        if (p.Spec.Egress != null)
            info.EgressRules = p.Spec.Egress.Select(r => new EgressRuleInfo
            {
                To = r.To?.Select(MapPeer).ToList() ?? [],
                Ports = MapPorts(r.Ports)
            }).ToList();

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
