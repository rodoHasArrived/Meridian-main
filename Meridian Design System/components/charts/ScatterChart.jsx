// Meridian ScatterChart — X/Y relationship across a sample (spread vs vol, factor exposure,
// tracking error vs return). History points fade in with recency; an optional highlighted
// "current" point sits over the cloud; an optional least-squares trendline overlays.
// Renders through ChartFrame (real-pixel, shared nice ticks on both axes).
import React from "react";
import { niceTicks, domainOf } from "./viz.js";
import { ChartFrame, AXIS_FONT } from "./ChartFrame.jsx";

function linreg(points) {
  const n = points.length;
  if (n < 2) return null;
  let sx = 0, sy = 0, sxx = 0, sxy = 0;
  for (const p of points) { sx += p.x; sy += p.y; sxx += p.x * p.x; sxy += p.x * p.y; }
  const denom = n * sxx - sx * sx;
  if (denom === 0) return null;
  const b = (n * sxy - sx * sy) / denom;
  const a = (sy - b * sx) / n;
  return { a, b };
}

export function ScatterChart({ ariaLabel, ariaDescription, accessibleData,
  points = [],                 // [{x, y}]
  current = null,
  trendline = true,
  xFmt = (v) => v.toFixed(0),
  yFmt = (v) => v.toFixed(0),
  xTicks = 6,
  yTicks = 5,
  fadeByRecency = true,
  pointColor = "var(--chart-secondary)",
  currentColor = "var(--orange)",
  height = 300,
}) {
  const allX = points.map((p) => p.x).concat(current ? [current.x] : []);
  const allY = points.map((p) => p.y).concat(current ? [current.y] : []);
  const [xLo, xHi] = domainOf([allX], 0.06);
  const yDomain = domainOf([allY], 0.08);
  const fit = trendline ? linreg(points) : null;
  const n = points.length;
  return (
    <ChartFrame ariaLabel={ariaLabel} ariaDescription={ariaDescription} accessibleData={accessibleData} height={height} n={Math.max(1, n)} railSide="left" rail={46} padL={14} padT={16} axisB={24} leftRule
      panes={[{ key: "y", domain: yDomain, ticks: yTicks, fmt: yFmt }]}>
      {(g) => {
        const Y = g.pane("y");
        const px = (x) => g.plotL + ((x - xLo) / (xHi - xLo || 1)) * (g.plotR - g.plotL);
        return (
          <g>
            {niceTicks(xLo, xHi, xTicks).filter((v) => v >= xLo && v <= xHi).map((v, i) => (
              <text key={"x" + i} x={px(v)} y={height - 8} fill="var(--chart-axis)" textAnchor="middle" style={AXIS_FONT}>{xFmt(v)}</text>
            ))}
            {fit && (
              <line x1={px(xLo)} x2={px(xHi)} y1={Y.y(fit.a + fit.b * xLo)} y2={Y.y(fit.a + fit.b * xHi)}
                stroke="var(--brand-signal,#C56343)" strokeWidth="1.4" opacity="0.8" />
            )}
            {points.map((p, i) => {
              const op = fadeByRecency && n > 1 ? 0.16 + 0.58 * (i / (n - 1)) : 0.55;
              return <circle key={i} cx={px(p.x)} cy={Y.y(p.y)} r="3" fill={pointColor} opacity={op} />;
            })}
            {current && (
              <circle cx={px(current.x)} cy={Y.y(current.y)} r="5.5" fill={currentColor} stroke="var(--bg-light)" strokeWidth="1.5" />
            )}
          </g>
        );
      }}
    </ChartFrame>
  );
}

ScatterChart.displayName = "ScatterChart";
