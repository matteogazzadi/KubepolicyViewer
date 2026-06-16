window.policyGraph = {
    network: null,

    init: function (containerId, graphData) {
        const container = document.getElementById(containerId);
        if (!container) return;

        const nodes = new vis.DataSet(graphData.nodes);
        const edges = new vis.DataSet(graphData.edges);

        const options = {
            nodes: {
                font: { size: 13, multi: false },
                borderWidth: 2,
                shadow: { enabled: true, size: 5, x: 2, y: 2 }
            },
            edges: {
                arrows: { to: { enabled: true, scaleFactor: 0.7 } },
                font: { size: 10, align: 'middle', background: 'rgba(255,255,255,0.7)' },
                smooth: { type: 'dynamic' },
                shadow: { enabled: true, size: 3 }
            },
            physics: {
                enabled: true,
                solver: 'barnesHut',
                stabilization: { iterations: 250, fit: true },
                barnesHut: {
                    gravitationalConstant: -6000,
                    centralGravity: 0.2,
                    springLength: 140,
                    springConstant: 0.04,
                    damping: 0.3
                }
            },
            interaction: {
                hover: true,
                tooltipDelay: 150,
                navigationButtons: true,
                keyboard: { enabled: true, speed: { x: 10, y: 10, zoom: 0.02 } },
                zoomView: true
            },
            groups: {
                pod: {
                    shape: 'dot',
                    size: 40,
                    color: { background: '#1a73e8', border: '#0d47a1', highlight: { background: '#4da3ff', border: '#0d47a1' }, hover: { background: '#2b82f8', border: '#0d47a1' } },
                    font: { color: '#fff', size: 14, bold: true }
                },
                ingress_source: {
                    shape: 'ellipse',
                    color: { background: '#2e7d32', border: '#1b5e20', highlight: { background: '#4caf50', border: '#1b5e20' }, hover: { background: '#388e3c', border: '#1b5e20' } },
                    font: { color: '#fff', size: 11 }
                },
                egress_dest: {
                    shape: 'ellipse',
                    color: { background: '#e65100', border: '#bf360c', highlight: { background: '#ff9800', border: '#bf360c' }, hover: { background: '#f57c00', border: '#bf360c' } },
                    font: { color: '#fff', size: 11 }
                },
                any_ingress: {
                    shape: 'diamond',
                    size: 30,
                    color: { background: '#ffd600', border: '#f9a825', highlight: { background: '#ffee58', border: '#f9a825' } },
                    font: { color: '#333', size: 12 }
                },
                any_egress: {
                    shape: 'diamond',
                    size: 30,
                    color: { background: '#ff6f00', border: '#e65100', highlight: { background: '#ffca28', border: '#e65100' } },
                    font: { color: '#fff', size: 12 }
                },
                blocked: {
                    shape: 'hexagon',
                    color: { background: '#c62828', border: '#7f0000', highlight: { background: '#ef5350', border: '#7f0000' } },
                    font: { color: '#fff', size: 11 }
                }
            }
        };

        if (this.network) {
            this.network.destroy();
            this.network = null;
        }

        this.network = new vis.Network(container, { nodes, edges }, options);

        this.network.once('stabilizationIterationsDone', () => {
            this.network.setOptions({ physics: { enabled: false } });
            this.network.fit({ animation: { duration: 600, easingFunction: 'easeInOutQuad' } });
        });
    },

    fit: function () {
        if (this.network) {
            this.network.fit({ animation: { duration: 500, easingFunction: 'easeInOutQuad' } });
        }
    },

    destroy: function () {
        if (this.network) {
            this.network.destroy();
            this.network = null;
        }
    }
};
