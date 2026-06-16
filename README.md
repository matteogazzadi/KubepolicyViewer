# KubepolicyViewer

A .NET 10 Blazor Server web application that connects to your local Kubernetes cluster and lets you **select a pod in a namespace** to visualize exactly which NetworkPolicies control its ingress and egress traffic.

## Features

- **Namespace & pod selector** — browse all namespaces and pods from `~/.kube/config`
- **Policy matching** — detects every `NetworkPolicy` whose `podSelector` matches the selected pod's labels (supports `matchLabels` and `matchExpressions`)
- **Interactive graph** (vis-network) — live directed graph showing:
  - The pod as the center node (blue)
  - Ingress sources as green nodes with arrows → pod
  - Egress destinations as orange nodes with arrows pod →
  - Unrestricted traffic shown as yellow diamond nodes
  - Blocked directions shown as red dashed edges
- **Detail table** — per-policy breakdown of ingress/egress rules, peer selectors, IP blocks, and allowed ports
- **Summary cards** — at-a-glance counts of matching policies and rules

## Prerequisites

- .NET 10 SDK
- Kubernetes cluster accessible via `~/.kube/config`

## Running

```bash
cd KubepolicyViewer
dotnet run
```

Then open http://localhost:5169

## Project structure

```
KubepolicyViewer/
├── Models/KubernetesModels.cs     # Data models (PodInfo, NetworkPolicyInfo, GraphData…)
├── Services/KubernetesService.cs  # Kubernetes API client + policy matching logic
├── Components/
│   ├── Pages/Home.razor           # Main UI (selectors, graph, detail table)
│   └── Layout/MainLayout.razor    # Shell layout
└── wwwroot/
    ├── app.css                    # Styling
    └── js/policyGraph.js          # vis-network graph initialization
```
