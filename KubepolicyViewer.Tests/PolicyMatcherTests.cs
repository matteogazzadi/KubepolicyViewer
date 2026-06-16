using k8s.Models;
using KubepolicyViewer.Services;

namespace KubepolicyViewer.Tests;

public class PolicyMatcherTests
{
    // ── Null / empty selector ────────────────────────────────────────────────

    [Fact]
    public void NullSelector_MatchesAnyPod()
    {
        var labels = new Dictionary<string, string> { ["app"] = "web" };
        Assert.True(PolicyMatcher.LabelsMatchSelector(labels, null));
    }

    [Fact]
    public void EmptySelector_MatchesAnyPod()
    {
        var labels = new Dictionary<string, string> { ["app"] = "web" };
        Assert.True(PolicyMatcher.LabelsMatchSelector(labels, new V1LabelSelector()));
    }

    [Fact]
    public void EmptySelector_MatchesPodWithNoLabels()
    {
        Assert.True(PolicyMatcher.LabelsMatchSelector([], new V1LabelSelector()));
    }

    // ── matchLabels ──────────────────────────────────────────────────────────

    [Fact]
    public void MatchLabels_ExactMatch_ReturnsTrue()
    {
        var labels = new Dictionary<string, string> { ["app"] = "web", ["env"] = "prod" };
        var selector = new V1LabelSelector
        {
            MatchLabels = new Dictionary<string, string> { ["app"] = "web" }
        };
        Assert.True(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void MatchLabels_AllPairsMatch_ReturnsTrue()
    {
        var labels = new Dictionary<string, string> { ["app"] = "web", ["env"] = "prod" };
        var selector = new V1LabelSelector
        {
            MatchLabels = new Dictionary<string, string> { ["app"] = "web", ["env"] = "prod" }
        };
        Assert.True(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void MatchLabels_WrongValue_ReturnsFalse()
    {
        var labels = new Dictionary<string, string> { ["app"] = "web" };
        var selector = new V1LabelSelector
        {
            MatchLabels = new Dictionary<string, string> { ["app"] = "api" }
        };
        Assert.False(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void MatchLabels_MissingKey_ReturnsFalse()
    {
        var labels = new Dictionary<string, string> { ["env"] = "prod" };
        var selector = new V1LabelSelector
        {
            MatchLabels = new Dictionary<string, string> { ["app"] = "web" }
        };
        Assert.False(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    // ── matchExpressions: In ─────────────────────────────────────────────────

    [Fact]
    public void MatchExpressions_In_ValuePresent_ReturnsTrue()
    {
        var labels = new Dictionary<string, string> { ["env"] = "prod" };
        var selector = new V1LabelSelector
        {
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "env", OperatorProperty = "In", Values = ["prod", "staging"] }
            ]
        };
        Assert.True(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void MatchExpressions_In_ValueAbsent_ReturnsFalse()
    {
        var labels = new Dictionary<string, string> { ["env"] = "dev" };
        var selector = new V1LabelSelector
        {
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "env", OperatorProperty = "In", Values = ["prod", "staging"] }
            ]
        };
        Assert.False(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void MatchExpressions_In_KeyMissing_ReturnsFalse()
    {
        var labels = new Dictionary<string, string> { ["app"] = "web" };
        var selector = new V1LabelSelector
        {
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "env", OperatorProperty = "In", Values = ["prod"] }
            ]
        };
        Assert.False(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    // ── matchExpressions: NotIn ──────────────────────────────────────────────

    [Fact]
    public void MatchExpressions_NotIn_ValueAbsent_ReturnsTrue()
    {
        var labels = new Dictionary<string, string> { ["env"] = "dev" };
        var selector = new V1LabelSelector
        {
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "env", OperatorProperty = "NotIn", Values = ["prod"] }
            ]
        };
        Assert.True(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void MatchExpressions_NotIn_KeyMissing_ReturnsTrue()
    {
        var labels = new Dictionary<string, string> { ["app"] = "web" };
        var selector = new V1LabelSelector
        {
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "env", OperatorProperty = "NotIn", Values = ["prod"] }
            ]
        };
        Assert.True(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void MatchExpressions_NotIn_ValuePresent_ReturnsFalse()
    {
        var labels = new Dictionary<string, string> { ["env"] = "prod" };
        var selector = new V1LabelSelector
        {
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "env", OperatorProperty = "NotIn", Values = ["prod"] }
            ]
        };
        Assert.False(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    // ── matchExpressions: Exists / DoesNotExist ──────────────────────────────

    [Fact]
    public void MatchExpressions_Exists_KeyPresent_ReturnsTrue()
    {
        var labels = new Dictionary<string, string> { ["app"] = "web" };
        var selector = new V1LabelSelector
        {
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "app", OperatorProperty = "Exists" }
            ]
        };
        Assert.True(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void MatchExpressions_Exists_KeyMissing_ReturnsFalse()
    {
        var labels = new Dictionary<string, string> { ["env"] = "prod" };
        var selector = new V1LabelSelector
        {
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "app", OperatorProperty = "Exists" }
            ]
        };
        Assert.False(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void MatchExpressions_DoesNotExist_KeyMissing_ReturnsTrue()
    {
        var labels = new Dictionary<string, string> { ["env"] = "prod" };
        var selector = new V1LabelSelector
        {
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "app", OperatorProperty = "DoesNotExist" }
            ]
        };
        Assert.True(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void MatchExpressions_DoesNotExist_KeyPresent_ReturnsFalse()
    {
        var labels = new Dictionary<string, string> { ["app"] = "web" };
        var selector = new V1LabelSelector
        {
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "app", OperatorProperty = "DoesNotExist" }
            ]
        };
        Assert.False(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    // ── Combined matchLabels + matchExpressions ──────────────────────────────

    [Fact]
    public void Combined_AllMatch_ReturnsTrue()
    {
        var labels = new Dictionary<string, string> { ["app"] = "web", ["env"] = "prod" };
        var selector = new V1LabelSelector
        {
            MatchLabels = new Dictionary<string, string> { ["app"] = "web" },
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "env", OperatorProperty = "In", Values = ["prod", "staging"] }
            ]
        };
        Assert.True(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void Combined_LabelsMismatch_ReturnsFalse()
    {
        var labels = new Dictionary<string, string> { ["app"] = "api", ["env"] = "prod" };
        var selector = new V1LabelSelector
        {
            MatchLabels = new Dictionary<string, string> { ["app"] = "web" },
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "env", OperatorProperty = "In", Values = ["prod"] }
            ]
        };
        Assert.False(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    [Fact]
    public void Combined_ExpressionMismatch_ReturnsFalse()
    {
        var labels = new Dictionary<string, string> { ["app"] = "web", ["env"] = "dev" };
        var selector = new V1LabelSelector
        {
            MatchLabels = new Dictionary<string, string> { ["app"] = "web" },
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "env", OperatorProperty = "In", Values = ["prod"] }
            ]
        };
        Assert.False(PolicyMatcher.LabelsMatchSelector(labels, selector));
    }

    // ── SelectorToString ─────────────────────────────────────────────────────

    [Fact]
    public void SelectorToString_Null_ReturnsStar()
    {
        Assert.Equal("*", PolicyMatcher.SelectorToString(null));
    }

    [Fact]
    public void SelectorToString_Empty_ReturnsStar()
    {
        Assert.Equal("*", PolicyMatcher.SelectorToString(new V1LabelSelector()));
    }

    [Fact]
    public void SelectorToString_MatchLabels_FormatsCorrectly()
    {
        var selector = new V1LabelSelector
        {
            MatchLabels = new Dictionary<string, string> { ["app"] = "web" }
        };
        Assert.Equal("app=web", PolicyMatcher.SelectorToString(selector));
    }

    [Fact]
    public void SelectorToString_MatchExpression_FormatsCorrectly()
    {
        var selector = new V1LabelSelector
        {
            MatchExpressions =
            [
                new V1LabelSelectorRequirement { Key = "env", OperatorProperty = "In", Values = ["prod", "staging"] }
            ]
        };
        Assert.Equal("env in [prod,staging]", PolicyMatcher.SelectorToString(selector));
    }

    // ── HasSelectors ─────────────────────────────────────────────────────────

    [Fact]
    public void HasSelectors_Null_ReturnsFalse() =>
        Assert.False(PolicyMatcher.HasSelectors(null));

    [Fact]
    public void HasSelectors_EmptySelector_ReturnsFalse() =>
        Assert.False(PolicyMatcher.HasSelectors(new V1LabelSelector()));

    [Fact]
    public void HasSelectors_WithMatchLabels_ReturnsTrue()
    {
        var sel = new V1LabelSelector { MatchLabels = new Dictionary<string, string> { ["x"] = "y" } };
        Assert.True(PolicyMatcher.HasSelectors(sel));
    }
}
