// Meridian DrawdownChart — the underwater plot. Drawdown is peak-to-trough decline, always
// ≤ 0; the curve hangs below the 0% waterline with a filled area, an optional recovery
// threshold rule, and a marker on the maximum drawdown. Pair beside an EquityCurve.
// Renders through ChartFrame: real-pixel geometry, shared ticks.
import React from "react";
import { linePath } from "./viz.js";
import { ChartFrame, AXIS_FONT } from "./ChartFrame.jsx";

export function DrawdownChart({ ariaLabel, ariaDescription, accessibleData,
  series,                      // number[] of drawdown %, 0 or negative (e.g. -12.4)
  labels = [],
  threshold = null,            // e.g. -10 → dashed warning rule
  valueFmt = (v) => `${v.toFixed(0)}%`,
  markMax = true,
  valueTicks = 5,
  timeTicks = 7,
  height = 300,
  rail = 56,
}) {
  const n = series.length;
  const dd = series.map((v) => Math.min(0, v));
  const lo = Math.min(...dd, threshold != null ? threshold : 0) * 1.08 || -1;
  let maxIdx = 0;
  for (let i = 1; i < n; i++) if (dd[i] < dd[maxIdx]) maxIdx = i;
  return (
    <ChartFrame ariaLabel={ariaLabel} ariaDescription={ariaDescription} accessibleData={accessibleData} height={height} n={n} xLabels={labels} xTicks={timeTicks} rail={rail} padL={8}
      panes={[{ key: "dd", domain: [lo, 0], ticks: valueTicks, fmt: valueFmt, zero: 0 }]}>
      {(g) => {
        const D = g.pane("dd");
        const path = linePath(dd, g.x, D.y);
        return (
          <g>
            <path d={`M${g.x(0).toFixed(1)} ${D.y(0).toFixed(1)}${dd.map((v, i) => `L${g.x(i).toFixed(1)} ${D.y(v).toFixed(1)}`).join("")}L${g.x(n - 1).toFixed(1)} ${D.y(0).toFixed(1)}Z`} fill="var(--chart-drawdown)" opacity="0.18" />
            <path d={path} fill="none" stroke="var(--chart-drawdown)" strokeWidth="1.6" />
            {threshold != null && (
              <g>
                <line x1={g.plotL} x2={g.plotR} y1={D.y(threshold)} y2={D.y(threshold)} stroke="var(--chart-warning)" strokeWidth="1.2" strokeDasharray="5 4" opacity="0.85" />
                <text x={g.plotL + 4} y={D.y(threshold) - 5} fill="var(--chart-warning)" style={{ ...AXIS_FONT, fontWeight: 600 }}>{valueFmt(threshold)} limit</text>
              </g>
            )}
            {markMax && n > 0 && (
              <g>
                <line x1={g.x(maxIdx)} x2={g.x(maxIdx)} y1={D.y(0)} y2={D.y(dd[maxIdx])} stroke="var(--chart-drawdown)" strokeWidth="1" strokeDasharray="3 3" opacity="0.7" />
                <circle cx={g.x(maxIdx)} cy={D.y(dd[maxIdx])} r="3.5" fill="var(--chart-drawdown)" />
                <rect x={Math.min(g.x(maxIdx) + 6, g.plotR - 70)} y={D.y(dd[maxIdx]) - 9} width="66" height="18" fill="var(--chart-drawdown)" opacity="0.95" />
                <text x={Math.min(g.x(maxIdx) + 6, g.plotR - 70) + 33} y={D.y(dd[maxIdx]) + 4} fill="var(--text-on-fill,#fff)" textAnchor="middle" style={{ ...AXIS_FONT, fontWeight: 600 }}>max {valueFmt(dd[maxIdx])}</text>
              </g>
            )}
          </g>
        );
      }}
    </ChartFrame>
  );
}

DrawdownChart.displayName = "DrawdownChart";
