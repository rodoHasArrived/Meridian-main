// Meridian Histogram — distribution of a sample (daily returns, slippage, fill latency).
// Pass raw `values` (auto-binned) or precomputed `bins`. Bars straddling zero tint red/green
// so a returns distribution reads at a glance; optional mean rule. Flat, token-driven.
import React from "react";

function buildBins(values, binCount) {
  const min = Math.min(...values), max = Math.max(...values);
  const span = (max - min) || 1;
  const w = span / binCount;
  const bins = Array.from({ length: binCount }, (_, i) => ({
    x0: min + i * w, x1: min + (i + 1) * w, count: 0,
  }));
  for (const v of values) {
    let i = Math.floor((v - min) / w);
    if (i >= binCount) i = binCount - 1;
    if (i < 0) i = 0;
    bins[i].count += 1;
  }
  return bins;
}

export function Histogram({
  values = null,
  bins: presetBins = null,
  binCount = 24,
  signed = true,                 // tint bins by sign of their center (returns)
  showMean = true,
  mean: meanProp = null,
  valueFmt = (v) => v.toFixed(1),
  countFmt = (c) => String(c),
  xTicks = 7,
}) {
  const bins = presetBins || (values && values.length ? buildBins(values, binCount) : []);
  const W = 960, H = 340;
  const padT = 14, axisB = 28, axisL = 40, axisR = 10;
  const plotL = axisL, plotR = W - axisR, plotT = padT, plotB = H - axisB;
  const n = bins.length;
  const maxCount = Math.max(1, ...bins.map((b) => b.count));
  const lo = n ? bins[0].x0 : 0, hi = n ? bins[n - 1].x1 : 1;
  const span = (hi - lo) || 1;

  const bx = (v) => plotL + ((v - lo) / span) * (plotR - plotL);
  const by = (c) => plotB - (c / maxCount) * (plotB - plotT);
  const gap = 1.5;

  const mean = meanProp != null ? meanProp
    : (values && values.length ? values.reduce((a, b) => a + b, 0) / values.length : null);

  const cTicks = [0, 0.25, 0.5, 0.75, 1].map((f) => Math.round(f * maxCount));
  const labelFont = { fontFamily: "var(--font-data)", fontSize: 11 };
  const tStep = Math.max(1, Math.round(n / xTicks));

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" style={{ width: "100%", height: "100%", display: "block" }}>
      <rect x="0" y="0" width={W} height={H} fill="var(--chart-plot)" />
      {[...new Set(cTicks)].map((c, k) => (
        <g key={k}>
          <line x1={plotL} x2={plotR} y1={by(c)} y2={by(c)} stroke="var(--chart-grid)" strokeWidth="0.8" opacity="0.55" />
          <text x={plotL - 6} y={by(c) + 4} fill="var(--chart-axis)" textAnchor="end" style={labelFont}>{countFmt(c)}</text>
        </g>
      ))}

      {bins.map((b, i) => {
        const center = (b.x0 + b.x1) / 2;
        const color = !signed ? "var(--chart-primary)" : (center < 0 ? "var(--chart-drawdown)" : "var(--chart-equity)");
        const x0 = bx(b.x0) + gap, x1 = bx(b.x1) - gap;
        const top = by(b.count);
        return (
          <rect key={i} x={x0} y={top} width={Math.max(0.5, x1 - x0)} height={plotB - top}
            fill={color} opacity="0.75" />
        );
      })}

      {/* x labels at bin edges */}
      {bins.map((b, i) => (i % tStep === 0) ? (
        <text key={"x" + i} x={bx(b.x0)} y={H - 9} fill="var(--chart-axis)" textAnchor="middle" style={labelFont}>{valueFmt(b.x0)}</text>
      ) : null)}
      {n > 0 && <text x={bx(bins[n - 1].x1)} y={H - 9} fill="var(--chart-axis)" textAnchor="middle" style={labelFont}>{valueFmt(bins[n - 1].x1)}</text>}

      {/* zero reference */}
      {signed && lo < 0 && hi > 0 && (
        <line x1={bx(0)} x2={bx(0)} y1={plotT} y2={plotB} stroke="var(--chart-border)" strokeWidth="1" opacity="0.5" />
      )}

      {/* mean rule */}
      {showMean && mean != null && mean >= lo && mean <= hi && (
        <g>
          <line x1={bx(mean)} x2={bx(mean)} y1={plotT} y2={plotB} stroke="var(--chart-crosshair)" strokeWidth="1.2" strokeDasharray="5 3" />
          <rect x={Math.min(bx(mean) + 5, plotR - 78)} y={plotT} width="74" height="17" fill="var(--chart-crosshair)" opacity="0.95" />
          <text x={Math.min(bx(mean) + 5, plotR - 78) + 37} y={plotT + 12} fill="#fff" textAnchor="middle" style={{ ...labelFont, fontWeight: 600 }}>μ {valueFmt(mean)}</text>
        </g>
      )}

      <line x1={plotL} x2={plotR} y1={plotB} y2={plotB} stroke="var(--chart-border)" strokeWidth="1.2" />
      <line x1={plotL} x2={plotL} y1={plotT} y2={plotB} stroke="var(--chart-border)" strokeWidth="1" opacity="0.5" />
    </svg>
  );
}

Histogram.displayName = "Histogram";
