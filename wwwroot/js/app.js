// Rust Server Monitor — compiled from wwwroot/ts/app.ts
// To recompile: tsc  (requires Node + TypeScript: npm install -g typescript)
var RustMonitor;
(function (RustMonitor) {
    let _chart = null;

    function updateChart(canvasId, labels, data) {
        const canvas = document.getElementById(canvasId);
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
                datasets: [{
                    label: "Players Online",
                    data,
                    borderColor: "#ff6b35",
                    backgroundColor: "rgba(255, 107, 53, 0.12)",
                    borderWidth: 2,
                    pointRadius: 3,
                    pointBackgroundColor: "#ff6b35",
                    tension: 0.35,
                    fill: true,
                }],
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 400 },
                scales: {
                    y: {
                        beginAtZero: true,
                        suggestedMax: 5,
                        ticks: { stepSize: 1, color: "rgba(255,255,255,0.45)", font: { size: 11 } },
                        grid: { color: "rgba(255,255,255,0.07)" },
                        border: { color: "rgba(255,255,255,0.1)" },
                    },
                    x: {
                        ticks: { color: "rgba(255,255,255,0.45)", font: { size: 11 }, maxRotation: 0, autoSkip: true, maxTicksLimit: 12 },
                        grid: { color: "rgba(255,255,255,0.04)" },
                        border: { color: "rgba(255,255,255,0.1)" },
                    },
                },
                plugins: {
                    legend: { labels: { color: "rgba(255,255,255,0.7)", font: { size: 12 }, boxWidth: 12 } },
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
    RustMonitor.updateChart = updateChart;

    function scrollToBottom(elementId) {
        const el = document.getElementById(elementId);
        if (el) el.scrollTop = el.scrollHeight;
    }
    RustMonitor.scrollToBottom = scrollToBottom;

    function copyToClipboard(text) {
        return (navigator.clipboard && navigator.clipboard.writeText(text)) || Promise.resolve();
    }
    RustMonitor.copyToClipboard = copyToClipboard;

    function initStatusPulse(selector) {
        const el = document.querySelector(selector);
        if (el) el.classList.add("status-pulse");
    }
    RustMonitor.initStatusPulse = initStatusPulse;
})(RustMonitor || (RustMonitor = {}));

window.rustMonitor = RustMonitor;
