// Rust Server Monitor — browser-side TypeScript
// Compiled to wwwroot/js/app.js

declare const Chart: any;

namespace RustMonitor {
    let _chart: any = null;

    /**
     * Creates or re-initialises the Chart.js player-count line chart.
     */
    export function updateChart(canvasId: string, labels: string[], data: number[]): void {
        const canvas = document.getElementById(canvasId) as HTMLCanvasElement | null;
        if (!canvas) return;

        if (_chart) {
            _chart.data.labels = labels;
            _chart.data.datasets[0].data = data;
            _chart.update("none");
            return;
        }

        _chart = new Chart(canvas, {
            type: "line",
            data: {
                labels,
                datasets: [
                    {
                        label: "Players Online",
                        data,
                        borderColor: "#ff6b35",
                        backgroundColor: "rgba(255, 107, 53, 0.12)",
                        borderWidth: 2,
                        pointRadius: 3,
                        pointBackgroundColor: "#ff6b35",
                        tension: 0.35,
                        fill: true,
                    },
                ],
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 400 },
                scales: {
                    y: {
                        beginAtZero: true,
                        suggestedMax: 5,
                        ticks: {
                            stepSize: 1,
                            color: "rgba(255,255,255,0.45)",
                            font: { size: 11 },
                        },
                        grid: { color: "rgba(255,255,255,0.07)" },
                        border: { color: "rgba(255,255,255,0.1)" },
                    },
                    x: {
                        ticks: {
                            color: "rgba(255,255,255,0.45)",
                            font: { size: 11 },
                            maxRotation: 0,
                            autoSkip: true,
                            maxTicksLimit: 12,
                        },
                        grid: { color: "rgba(255,255,255,0.04)" },
                        border: { color: "rgba(255,255,255,0.1)" },
                    },
                },
                plugins: {
                    legend: {
                        labels: {
                            color: "rgba(255,255,255,0.7)",
                            font: { size: 12 },
                            boxWidth: 12,
                        },
                    },
                    tooltip: {
                        backgroundColor: "#1a1a2e",
                        borderColor: "rgba(255,107,53,0.5)",
                        borderWidth: 1,
                        titleColor: "#ff6b35",
                        bodyColor: "rgba(255,255,255,0.8)",
                    },
                },
            },
        });
    }

    /**
     * Scrolls an element to its bottom (used for the live log view).
     */
    export function scrollToBottom(elementId: string): void {
        const el = document.getElementById(elementId);
        if (el) el.scrollTop = el.scrollHeight;
    }

    /**
     * Copies text to the clipboard.
     */
    export function copyToClipboard(text: string): Promise<void> {
        return navigator.clipboard?.writeText(text) ?? Promise.resolve();
    }

    /**
     * Animates the status indicator badge (called once on first render).
     */
    export function initStatusPulse(selector: string): void {
        const el = document.querySelector(selector) as HTMLElement | null;
        if (el) el.classList.add("status-pulse");
    }
}

(window as any).rustMonitor = RustMonitor;
