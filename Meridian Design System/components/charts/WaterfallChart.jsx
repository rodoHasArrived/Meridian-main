// Meridian WaterfallChart — additive bridge from a starting level through signed contributions
// to a net (P&L attribution: gross → carry → funding → fees → net; NAV bridges). Items are
// `start` (absolute), `delta` (floats from the running level), or `total` (drops to zero);
// a computed Net total is appended unless one is provided. Flat, token-driven.
import React from "react";

function niceTicks(lo, hi, n) {
  const span = hi - lo || 1;
  const raw = span / Math.max(1, n);
  const p = Math.pow(10, Math.floor(Math.log10(raw)));
  const step = [1, 2, 2.5, 5, 10].map((m) => m * p).find((c) => c >= raw) || 10 * p;
  const out = [];
  for (let v = Math.ceil(lo / step) * step; v <= hi + step * 1e-6; v += step) out.push(+v.toFixed(10));
  return out;
}

export function WaterfallChart({
  items = [],                    // [{ label, value, kind?: "start" | "delta" | "total" }]
  showTotal = true,
  totalLabel = "Net",
  valueFmt = (v) => v.toFixed(1),
  deltaFmt = null,               // default: explicit-sign valueFmt
  valueTicks = 5,
  showConnectors = true,
}) {
  const dFmt = deltaFmt || ((v) => (v < 0 ? "\u2212" : "+") + valueFmt(Math.abs(v)));

  // Resolve each item to a [from, to] span and a running level.
  let cum = 0;
  const steps = items.map((it) => {
    const kind = it.kind || "delta";
    if (kind === "start") { cum = it.value; return { ...it, kind, from: 0, to: it.value, level: cum }; }
    if (kind === "total") { return { ...it, kind, from: 0, to: cum, level: cum, value: cum }; }
    const from = cum; cum += it.value;
    return { ...it, kind, from, to: cum, level: cum };
  });
  if (showTotal && !items.some((it) => it.kind === "total")) {
    steps.push({ label: totalLabel, kind: "total", from: 0, to: cum, level: cum, value: cum });
  }

  const W = 960, H = 340;
  const padT = 22, axisB = 26, axisL = 50, axisR = 10;
  const plotL = axisL, plotR = W - axisR, plotT = padT, plotB = H - axisB;
  const n = steps.length || 1;
  const edges = steps.flatMap((s) => [s.from, s.to]);
  const lo0 = Math.min(0, ...edges), hi0 = Math.max(0, ...edges);
  const pad = (hi0 - lo0 || 1) * 0.09;
  const lo = lo0 < 0 ? lo0 - pad : lo0, hi = hi0 + pad;
  const span = (hi - lo) || 1;
  const y = (v) => plotB - ((v - lo) / span) * (plotB - plotT);
  const band = (plotR - plotL) / n;
  const barW = Math.min(band * 0.58, 84);
  const ticks = niceTicks(lo, hi, valueTicks);
  const labelFont = { fontFamily: "var(--font-data)", fontSize: 11 };

  const color = (s) => {
    if (s.color) return s.color;
    if (s.kind !== "delta") return "var(--chart-primary)";
    return s.value < 0 ? "var(--chart-drawdown)" : "var(--chart-equity)";
  };

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" style={{ width: "100%", height: "100%", display: "block" }}>
      <rect x="0" y="0" width={W} height={H} fill="var(--chart-plot)" />
      {ticks.map((t, k) => (
        <g key={k}>
          <line x1={plotL} x2={plotR} y1={y(t)} y2={y(t)} stroke="var(--chart-grid)" strokeWidth="0.8" opacity="0.55" />
          <text x={plotL - 6} y={y(t) + 4} fill="var(--chart-axis)" textAnchor="end" style={labelFont}>{valueFmt(t)}</text>
        </g>
      ))}

      {/* connectors: carry the running level across each gap */}
      {showConnectors && steps.slice(0, -1).map((s, i) => {
        const x0 = plotL + band * i + band / 2 + barW / 2;
        const x1 = plotL + band * (i + 1) + band / 2 - barW / 2;
        const yy = y(steps[i + 1].kind === "total" ? steps[i + 1].to : s.to);
        return <line key={"c" + i} x1={x0} x2={x1} y1={yy} y2={yy}
          stroke="var(--chart-axis)" strokeWidth="0.9" strokeDasharray="3 3" opacity="0.6" />;
      })}

      {steps.map((s, i) => {
        const cx = plotL + band * i + band / 2;
        const top = Math.min(y(s.from), y(s.to)), bot = Math.max(y(s.from), y(s.to));
        const labelUp = s.kind === "delta" ? s.value >= 0 : s.to >= 0;
        return (
          <g key={i}>
            <rect x={cx - barW / 2} y={top} width={barW} height={Math.max(0.75, bot - top)}
              fill={color(s)} opacity="0.78" />
            <text x={cx} y={labelUp ? top - 5 : bot + 13} fill="var(--text-secondary)" textAnchor="middle"
              style={{ ...labelFont, fontWeight: 600 }}>
              {s.kind === "delta" ? dFmt(s.value) : valueFmt(s.to)}
            </text>
            <text x={cx} y={H - 9} fill="var(--chart-axis)" textAnchor="middle" style={{ ...labelFont, fontSize: 10.5 }}>{s.label}</text>
          </g>
        );
      })}

      {/* zero line + frame */}
      <line x1={plotL} x2={plotR} y1={y(Math.max(lo, Math.min(hi, 0)))} y2={y(Math.max(lo, Math.min(hi, 0)))}
        stroke="var(--chart-border)" strokeWidth="1.2" />
      <line x1={plotL} x2={plotL} y1={plotT} y2={plotB} stroke="var(--chart-border)" strokeWidth="1" opacity="0.5" />
    </svg>
  );
}

WaterfallChart.displayName = "WaterfallChart";
