// Dashboard workstation — the one piece of this template that has to be React: an SVG equity
// curve with a benchmark overlay and a last-value marker. Everything else (headings, KPI row,
// panels, table, banner) lives as editable markup in DashboardWorkstation.dc.html.
function DashboardEquityChart({ equity = [], bench = [], lastLabel = "+23.7%" }) {
  const w = 760, h = 180, gut = 60, plotW = w - gut, step = plotW / Math.max(1, equity.length - 1);
  const sy = (v) => v * 2.4;
  const pts = (arr) => arr.map((v, i) => `${(i * step).toFixed(1)},${sy(v).toFixed(1)}`).join(" ");
  const lastX = (equity.length - 1) * step, lastY = sy(equity[equity.length - 1]);
  const baseY = sy(equity[0]);                        // opening equity = cost basis
  const areaPts = `0,${h} ${pts(equity)} ${plotW},${h}`;
  return (
    <svg viewBox={`0 0 ${w} ${h}`} style={{ width: "100%", height: "auto", display: "block" }} aria-label="Equity curve vs benchmark">
      <g stroke="var(--chart-grid)" strokeWidth="1">
        {[36, 78, 120, 162].map((y) => <line key={y} x1="0" y1={y} x2={plotW} y2={y} />)}
      </g>
      <line x1="0" x2={plotW} y1={baseY} y2={baseY} stroke="var(--chart-axis)" strokeWidth="1" strokeDasharray="3 3" opacity="0.55" />
      <text x="3" y={baseY - 5} fill="var(--chart-axis)" style={{ font: "500 10px var(--font-data)" }}>cost basis</text>
      <polygon points={areaPts} fill="var(--chart-equity)" opacity="0.10" />
      <polyline fill="none" stroke="var(--chart-secondary)" strokeWidth="1.5" strokeDasharray="5 4" points={pts(bench)} />
      <polyline fill="none" stroke="var(--chart-equity)" strokeWidth="2" points={pts(equity)} />
      <line x1="0" x2={lastX} y1={lastY} y2={lastY} stroke="var(--chart-equity)" strokeWidth="0.8" strokeDasharray="2 3" opacity="0.5" />
      <circle cx={lastX} cy={lastY} r="3.5" fill="var(--chart-equity)" />
      <rect x={lastX + 6} y={lastY - 9} width="52" height="18" fill="var(--chart-equity)" />
      <text x={lastX + 32} y={lastY + 4} textAnchor="middle" fill="var(--text-on-fill, #fff)" style={{ font: "600 11px var(--font-data)", fontVariantNumeric: "slashed-zero tabular-nums" }}>{lastLabel}</text>
    </svg>
  );
}

window.DashboardEquityChart = DashboardEquityChart;
if (typeof module !== "undefined") module.exports = { DashboardEquityChart };
