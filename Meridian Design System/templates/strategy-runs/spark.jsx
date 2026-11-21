// Strategy Runs — equity sparkline fragment. Only the SVG chart stays React;
// the frame, ledger and trace are editable markup in StrategyRuns.dc.html.
function RunSpark({ pts, tone }) {
  const w = 240, h = 56, min = Math.min(...pts), max = Math.max(...pts), span = max - min || 1;
  const step = w / (pts.length - 1);
  const d = pts.map((p, i) => `${(i * step).toFixed(1)},${(h - 4 - ((p - min) / span) * (h - 8)).toFixed(1)}`).join(" ");
  return React.createElement("svg", { viewBox: `0 0 ${w} ${h}`, style: { width: "100%", height: "auto", display: "block" } },
    React.createElement("polyline", { fill: "none", stroke: tone, strokeWidth: 1.5, points: d }),
    React.createElement("circle", { cx: (pts.length - 1) * step, cy: h - 4 - ((pts[pts.length - 1] - min) / span) * (h - 8), r: 2.5, fill: tone }));
}
window.RunSpark = RunSpark;
if (typeof module !== "undefined") module.exports = { RunSpark };
