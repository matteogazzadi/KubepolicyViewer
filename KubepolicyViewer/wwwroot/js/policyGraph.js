(function () {
    const STAR_COUNT = 180;
    const stars = Array.from({ length: STAR_COUNT }, () => ({
        x: Math.random(),
        y: Math.random(),
        r: Math.random() * 1.1 + 0.3,
        phase: Math.random() * Math.PI * 2,
        speed: Math.random() * 0.7 + 0.2,
        bright: Math.random() > 0.82
    }));

    window.policyGraph = {
        network: null,
        _animFrame: null,

        init: function (containerId, graphData) {
            const container = document.getElementById(containerId);
            if (!container) return;

            this.destroy();

            const nodes = new vis.DataSet(graphData.nodes);
            const edges = new vis.DataSet(graphData.edges);

            const options = {
                nodes: {
                    font: { size: 12, face: "'JetBrains Mono', monospace", multi: false },
                    borderWidth: 1.5
                },
                edges: {
                    arrows: { to: { enabled: true, scaleFactor: 0.55 } },
                    font: {
                        size: 9, face: "'JetBrains Mono', monospace",
                        align: 'middle', color: '#6b84a4',
                        background: 'rgba(5,8,16,0.75)', strokeWidth: 0
                    },
                    smooth: { type: 'dynamic' },
                    width: 1.5,
                    selectionWidth: 2.5
                },
                physics: {
                    enabled: true,
                    solver: 'barnesHut',
                    stabilization: { iterations: 320, fit: true },
                    barnesHut: {
                        gravitationalConstant: -7000,
                        centralGravity: 0.14,
                        springLength: 165,
                        springConstant: 0.04,
                        damping: 0.3
                    }
                },
                interaction: {
                    hover: true,
                    tooltipDelay: 100,
                    navigationButtons: false,
                    keyboard: { enabled: true },
                    zoomView: true
                },
                groups: {
                    pod: {
                        shape: 'dot', size: 38,
                        color: {
                            background: '#001a0d',
                            border: '#00ff88',
                            highlight: { background: '#002a14', border: '#69f0ae' },
                            hover: { background: '#001e0d', border: '#00ff99' }
                        },
                        font: { color: '#00ff88', size: 13, bold: true },
                        shadow: { enabled: true, color: 'rgba(0,255,136,0.65)', size: 22, x: 0, y: 0 }
                    },
                    ingress_source: {
                        shape: 'ellipse',
                        color: {
                            background: '#001a0a',
                            border: '#00e676',
                            highlight: { background: '#00270f', border: '#69f0ae' },
                            hover: { background: '#001e0c', border: '#00e676' }
                        },
                        font: { color: '#00e676', size: 11 },
                        shadow: { enabled: true, color: 'rgba(0,230,118,0.45)', size: 16, x: 0, y: 0 }
                    },
                    egress_dest: {
                        shape: 'ellipse',
                        color: {
                            background: '#1a0a00',
                            border: '#ff6b35',
                            highlight: { background: '#2a1200', border: '#ff8c5a' },
                            hover: { background: '#220d00', border: '#ff7040' }
                        },
                        font: { color: '#ff6b35', size: 11 },
                        shadow: { enabled: true, color: 'rgba(255,107,53,0.45)', size: 16, x: 0, y: 0 }
                    },
                    any_ingress: {
                        shape: 'diamond', size: 28,
                        color: {
                            background: '#1a1500',
                            border: '#ffd740',
                            highlight: { background: '#262000', border: '#ffe57a' }
                        },
                        font: { color: '#ffd740', size: 11 },
                        shadow: { enabled: true, color: 'rgba(255,215,64,0.4)', size: 14, x: 0, y: 0 }
                    },
                    any_egress: {
                        shape: 'diamond', size: 28,
                        color: {
                            background: '#1a0d00',
                            border: '#ff6b35',
                            highlight: { background: '#261300', border: '#ff8c5a' }
                        },
                        font: { color: '#ff6b35', size: 11 },
                        shadow: { enabled: true, color: 'rgba(255,107,53,0.4)', size: 14, x: 0, y: 0 }
                    },
                    blocked: {
                        shape: 'hexagon',
                        color: {
                            background: '#1a0008',
                            border: '#ff3d5a',
                            highlight: { background: '#280010', border: '#ff6680' }
                        },
                        font: { color: '#ff3d5a', size: 11 },
                        shadow: { enabled: true, color: 'rgba(255,61,90,0.5)', size: 16, x: 0, y: 0 }
                    }
                }
            };

            this.network = new vis.Network(container, { nodes, edges }, options);

            const self = this;

            this.network.on('beforeDrawing', function (ctx) {
                const canvas = ctx.canvas;
                const w = canvas.width;
                const h = canvas.height;
                const t = Date.now() / 1000;

                ctx.save();
                ctx.setTransform(1, 0, 0, 1, 0, 0);

                const grad = ctx.createRadialGradient(w * 0.5, h * 0.4, 0, w * 0.5, h * 0.5, Math.max(w, h) * 0.9);
                grad.addColorStop(0, '#0a0f1e');
                grad.addColorStop(0.55, '#070c18');
                grad.addColorStop(1, '#050810');
                ctx.fillStyle = grad;
                ctx.fillRect(0, 0, w, h);

                for (const s of stars) {
                    const alpha = s.bright
                        ? 0.45 + 0.55 * Math.pow(Math.sin(t * s.speed + s.phase), 2)
                        : 0.15 + 0.25 * Math.pow(Math.sin(t * s.speed + s.phase), 2);
                    ctx.beginPath();
                    ctx.arc(s.x * w, s.y * h, s.r, 0, Math.PI * 2);
                    ctx.fillStyle = 'rgba(212,227,255,' + alpha.toFixed(3) + ')';
                    ctx.fill();
                }

                ctx.restore();
            });

            this.network.once('stabilizationIterationsDone', function () {
                self.network.setOptions({ physics: { enabled: false } });
                self.network.fit({ animation: { duration: 800, easingFunction: 'easeInOutQuad' } });
            });

            const animate = function () {
                if (self.network) {
                    self.network.redraw();
                    self._animFrame = requestAnimationFrame(animate);
                }
            };
            this._animFrame = requestAnimationFrame(animate);
        },

        fit: function () {
            if (this.network) {
                this.network.fit({ animation: { duration: 500, easingFunction: 'easeInOutQuad' } });
            }
        },

        destroy: function () {
            if (this._animFrame) {
                cancelAnimationFrame(this._animFrame);
                this._animFrame = null;
            }
            if (this.network) {
                this.network.destroy();
                this.network = null;
            }
        }
    };
})();
