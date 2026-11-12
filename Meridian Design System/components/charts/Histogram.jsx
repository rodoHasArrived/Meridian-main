// Meridian Histogram — distribution of a sample (daily returns, slippage, fill latency).
// Pass raw `values` (auto-binned) or precomputed `bins`. Bars straddling zero tint red/green
// so a returns distribution reads at a glance; optional mean rule. Renders through ChartFrame
// (real-pixel); bin x-positions stay value-scaled so non-uniform preset bins keep working.
import React from "react";
import { niceTicks } from "./viz.js";
import { ChartFrame, AXIS_FONT } from "./ChartFrame.jsx";

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

export function Histogram({ ariaLabel, ariaDescription, accessibleData,
  values = null,
  bins: presetBins = null,
  binCount = 24,
  signed = true,                 // tint bins by sign of their center (returns)
  showMean = true,
  mean: meanProp = null,
  valueFmt = (v) => v.toFixed(1),
  countFmt = (c) => String(c),
  xTicks = 7,
  height = 300,
}) {
  const bins = presetBins || (values && values.length ? buildBins(values, binCount) : []);
  const n = bins.length;
  const maxCount = Math.max(1, ...bins.map((b) => b.count));
  const lo = n ? bins[0].x0 : 0, hi = n ? bins[n - 1].x1 : 1;
  const span = (hi - lo) || 1;
  const mean = meanProp != null ? meanProp
    : (values && values.length ? values.reduce((a, b) => a + b, 0) / values.length : null);
  const tStep = Math.max(1, Math.round(n / xTicks));
  return (
    <ChartFrame ariaLabel={ariaLabel} ariaDescription={ariaDescription} accessibleData={accessibleData} height={height} n={Math.max(1, n)} railSide="left" rail={40} padL={10} axisB={28} leftRule
      panes={[{ key: "c", domain: [0, maxCount], ticks: 4, fmt: countFmt }]}>
      {(g) => {
        const C = g.pane("c");
        const bx = (v) => g.plotL + ((v - lo) / span) * (g.plotR - g.plotL);
        const gap = 1.5;
        return (
          <g>
            {bins.map((b, i) => {
              const center = (b.x0 + b.x1) / 2;
              const color = !signed ? "var(--chart-primary)" : (center < 0 ? "var(--chart-drawdown)" : "var(--chart-equity)");
              const x0 = bx(b.x0) + gap, x1 = bx(b.x1) - gap;
              const top = C.y(b.count);
              return (
                <rect key={i} x={x0} y={top} width={Math.max(0.5, x1 - x0)} height={C.b - top}
                  fill={color} opacity="0.75" />
              );
            })}
            {bins.map((b, i) => (i % tStep === 0) ? (
              <text key={"x" + i} x={bx(b.x0)} y={height - 9} fill="var(--chart-axis)" textAnchor="middle" style={AXIS_FONT}>{valueFmt(b.x0)}</text>
            ) : null)}
            {n > 0 && <text x={bx(bins[n - 1].x1)} y={height - 9} fill="var(--chart-axis)" textAnchor="middle" style={AXIS_FONT}>{valueFmt(bins[n - 1].x1)}</text>}
            {signed && lo < 0 && hi > 0 && (
              <line x1={bx(0)} x2={bx(0)} y1={C.t} y2={C.b} stroke="var(--chart-border)" strokeWidth="1" opacity="0.5" />
            )}
            {showMean && mean != null && mean >= lo && mean <= hi && (
              <g>
                <line x1={bx(mean)} x2={bx(mean)} y1={C.t} y2={C.b} stroke="var(--chart-crosshair)" strokeWidth="1.2" strokeDasharray="5 3" />
                <rect x={Math.min(bx(mean) + 5, g.plotR - 78)} y={C.t} width="74" height="17" fill="var(--chart-crosshair)" opacity="0.95" />
                <text x={Math.min(bx(mean) + 5, g.plotR - 78) + 37} y={C.t + 12} fill="var(--text-on-fill,#fff)" textAnchor="middle" style={{ ...AXIS_FONT, fontWeight: 600 }}>μ {valueFmt(mean)}</text>
              </g>
            )}
          </g>
        );
      }}
    </ChartFrame>
  );
}

Histogram.displayName = "Histogram";
