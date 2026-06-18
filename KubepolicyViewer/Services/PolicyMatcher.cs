using k8s.Models;
using KubepolicyViewer.Models;

namespace KubepolicyViewer.Services;

public static class PolicyMatcher
{
    public static bool LabelsMatchSelector(Dictionary<string, string> podLabels, V1LabelSelector? selector)
    {
        if (selector == null) return true;

        bool hasMatchLabels = selector.MatchLabels?.Count > 0;
        bool hasMatchExpressions = selector.MatchExpressions?.Count > 0;

        if (!hasMatchLabels && !hasMatchExpressions) return true;

        if (selector.MatchLabels != null)
        {
            foreach (var kv in selector.MatchLabels)
            {
                if (!podLabels.TryGetValue(kv.Key, out var val) || val != kv.Value)
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
                        if (!podLabels.TryGetValue(expr.Key, out var inVal)
                            || !(expr.Values ?? []).Contains(inVal))
                            return false;
                        break;
                    case "NotIn":
                        if (podLabels.TryGetValue(expr.Key, out var notInVal)
                            && (expr.Values ?? []).Contains(notInVal))
                            return false;
                        break;
                    case "Exists":
                        if (!podLabels.ContainsKey(expr.Key)) return false;
                        break;
                    case "DoesNotExist":
                        if (podLabels.ContainsKey(expr.Key)) return false;
                        break;
                }
            }
        }

        return true;
    }

    public static string SelectorToString(V1LabelSelector? selector)
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

    public static bool HasSelectors(V1LabelSelector? sel) =>
        sel != null && (sel.MatchLabels?.Count > 0 || sel.MatchExpressions?.Count > 0);

    /// <summary>
    /// Returns true if the given peer (from an ingress rule) matches the source pod.
    /// policyNamespace is the namespace where the NetworkPolicy lives (same as dstPod's namespace).
    /// </summary>
    public static bool PeerMatchesSource(
        V1NetworkPolicyPeer peer,
        Dictionary<string, string> srcPodLabels,
        Dictionary<string, string> srcNsLabels,
        string srcNamespace,
        string policyNamespace)
    {
        if (peer.IpBlock != null) return false;

        if (peer.NamespaceSelector != null)
        {
            if (!LabelsMatchSelector(srcNsLabels, peer.NamespaceSelector)) return false;
        }
        else if (srcNamespace != policyNamespace) return false;

        if (peer.PodSelector != null && !LabelsMatchSelector(srcPodLabels, peer.PodSelector))
            return false;

        return true;
    }

    /// <summary>
    /// Returns true if the given peer (from an egress rule) matches the destination pod.
    /// policyNamespace is the namespace where the NetworkPolicy lives (same as srcPod's namespace).
    /// </summary>
    public static bool PeerMatchesDestination(
        V1NetworkPolicyPeer peer,
        Dictionary<string, string> dstPodLabels,
        Dictionary<string, string> dstNsLabels,
        string dstNamespace,
        string policyNamespace)
    {
        if (peer.IpBlock != null) return false;

        if (peer.NamespaceSelector != null)
        {
            if (!LabelsMatchSelector(dstNsLabels, peer.NamespaceSelector)) return false;
        }
        else if (dstNamespace != policyNamespace) return false;

        if (peer.PodSelector != null && !LabelsMatchSelector(dstPodLabels, peer.PodSelector))
            return false;

        return true;
    }
}
