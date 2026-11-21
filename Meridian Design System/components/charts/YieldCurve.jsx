// Meridian YieldCurve — term-structure line chart (yield vs tenor) for rates & bond
// surfaces (Basket Builder, fixed-income research). Tenors position on a sqrt-of-years
// x-scale so the money-market end doesn't crush. Prior-period overlays recede down the
// series ladder (--chart-compare-1/2); scenario overlays render dashed --chart-scenario
// (terracotta) so a shock can never read as observed data. An optional tenor-pair spread
// readout ("2Y–10Y") prints top-left, red when inverted. Renders through ChartFrame.
import React from "react";
import { domainOf } from "./viz.js";
import { ChartFrame, AXIS_FONT } from "./ChartFrame.jsx";

const X_SCALES = {
  sqrt: (y) => Math.sqrt(y),
  linear: (y) => y,
};

export function YieldCurve({ ariaLabel, ariaDescription, accessibleData,
  points = [],                 // [{ tenor, years, value }]
  compare = null,
  overlays = null,
  label = "Current",
  compareLabel = "Prior",
  yFmt = (v) => v.toFixed(2) + "%",
  yTicks = 5,
  xScale = "sqrt",
  spread = null,               // { a, b } tenor names
  markers = true,
  lineColor = "var(--chart-primary)",
  compareColor = "var(--chart-secondary)",
  height = 300,
}) {
  const sc = typeof xScale === "function" ? xScale : X_SCALES[xScale] || X_SCALES.sqrt;
  const LADDER = ["var(--chart-compare-1)", "var(--chart-compare-2)", "var(--chart-secondary)"];
  const ovls = (overlays || (compare ? [{ points: compare, label: compareLabel, color: compareColor, dashed: true }] : []))
    .filter((o) => o && o.points && o.points.length)
    .map((o, i) => ({
      points: o.points,
      label: o.label || `Series ${i + 2}`,
      dashed: !!(o.dashed || o.scenario),
      color: o.color || (o.scenario ? "var(--chart-scenario)" : LADDER[Math.min(i, LADDER.length - 1)]),
    }));
  const all = ovls.reduce((a, o) => a.concat(o.points), points.slice());
  if (!all.length) return <div style={{ width: "100%", height }} />;
  const xVals = all.map((p) => sc(p.years));
  const xLo = Math.min(...xVals), xHi = Math.max(...xVals);
  const yDomain = domainOf([all.map((p) => p.value)], 0.14);
  return (
    <ChartFrame ariaLabel={ariaLabel} ariaDescription={ariaDescription} accessibleData={accessibleData} height={height} n={Math.max(1, points.length)} railSide="left" rail={52} padL={16} padT={18} leftRule
      panes={[{ key: "y", domain: yDomain, ticks: yTicks, fmt: yFmt }]}>
      {(g) => {
        const Y = g.pane("y");
        const px = (p) => g.plotL + ((sc(p.years) - xLo) / (xHi - xLo || 1)) * (g.plotR - g.plotL);
        const path = (pts) => pts.map((p, i) => `${i ? "L" : "M"}${px(p).toFixed(1)},${Y.y(p.value).toFixed(1)}`).join(" ");
        const series = (pts, color, dashed, r) => (
          <g>
            <path d={path(pts)} fill="none" stroke={color} strokeWidth="1.6"
              strokeDasharray={dashed ? "5 4" : undefined} />
            {markers && pts.map((p, i) => (
              <circle key={i} cx={px(p)} cy={Y.y(p.value)} r={r} fill={color} stroke="var(--chart-plot)" strokeWidth="1.2" />
            ))}
          </g>
        );
        let spreadNode = null;
        if (spread && spread.a && spread.b) {
          const pa = points.find((p) => p.tenor === spread.a);
          const pb = points.find((p) => p.tenor === spread.b);
          if (pa && pb) {
            const bp = Math.round((pb.value - pa.value) * 100);
            const neg = bp < 0;
            spreadNode = (
              <text x={g.plotL + 8} y={Y.t + 12} style={AXIS_FONT}
                fill={neg ? "var(--red)" : "var(--text-secondary)"}>
                {`${spread.a}–${spread.b} ${bp >= 0 ? "+" : "−"}${Math.abs(bp)}bp${neg ? " · inverted" : ""}`}
              </text>
            );
          }
        }
        return (
          <g>
            {points.map((p, i) => (
              <g key={"x" + i}>
                <line x1={px(p)} x2={px(p)} y1={g.innerB} y2={g.innerB + 4} stroke="var(--chart-border)" strokeWidth="1" />
                <text x={px(p)} y={height - 8} fill="var(--chart-axis)" textAnchor="middle" style={AXIS_FONT}>{p.tenor}</text>
              </g>
            ))}
            {ovls.map((o, i) => <g key={"ov" + i}>{series(o.points, o.color, o.dashed, 2.4)}</g>).reverse()}
            {series(points, lineColor, false, 3.2)}
            {spreadNode}
            {ovls.length > 0 && (
              <g>
                {[{ label, color: lineColor, dashed: false }].concat(ovls).map((s, i) => (
                  <g key={"lg" + i}>
                    <line x1={g.plotR - 150} x2={g.plotR - 128} y1={Y.t + 8 + i * 15} y2={Y.t + 8 + i * 15}
                      stroke={s.color} strokeWidth="1.6" strokeDasharray={s.dashed ? "5 4" : undefined} />
                    <text x={g.plotR - 122} y={Y.t + 12 + i * 15} fill="var(--text-secondary)" style={AXIS_FONT}>{s.label}</text>
                  </g>
                ))}
              </g>
            )}
          </g>
        );
      }}
    </ChartFrame>
  );
}

YieldCurve.displayName = "YieldCurve";
