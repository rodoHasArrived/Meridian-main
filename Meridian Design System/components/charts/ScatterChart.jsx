// Meridian ScatterChart — X/Y relationship across a sample (spread vs vol, factor exposure,
// tracking error vs return). History points fade in with recency; an optional highlighted
// "current" point sits over the cloud; an optional least-squares trendline overlays. Flat,
// token-driven, mirrors the Histogram/DrawdownChart plot conventions.
import React from "react";

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

export function ScatterChart({
  points = [],
  current = null,
  trendline = true,
  xFmt = (v) => v.toFixed(0),
  yFmt = (v) => v.toFixed(0),
  xTicks = 6,
  yTicks = 5,
  fadeByRecency = true,
  pointColor = "var(--chart-secondary)",
  currentColor = "var(--orange)",
}) {
  const W = 960, H = 340;
  const padT = 16, axisB = 24, axisL = 46, axisR = 14;
  const plotL = axisL, plotR = W - axisR, plotT = padT, plotB = H - axisB;

  const allX = points.map((p) => p.x).concat(current ? [current.x] : []);
  const allY = points.map((p) => p.y).concat(current ? [current.y] : []);
  const xMin = Math.min(...allX), xMax = Math.max(...allX);
  const yMin = Math.min(...allY), yMax = Math.max(...allY);
  const xPad = (xMax - xMin || 1) * 0.06, yPad = (yMax - yMin || 1) * 0.08;
  const xLo = xMin - xPad, xHi = xMax + xPad, yLo = yMin - yPad, yHi = yMax + yPad;

  const px = (x) => plotL + ((x - xLo) / (xHi - xLo || 1)) * (plotR - plotL);
  const py = (y) => plotB - ((y - yLo) / (yHi - yLo || 1)) * (plotB - plotT);

  const fit = trendline ? linreg(points) : null;
  const labelFont = { fontFamily: "var(--font-data)", fontSize: 11 };

  const xTickVals = Array.from({ length: xTicks + 1 }, (_, i) => xLo + (i / xTicks) * (xHi - xLo));
  const yTickVals = Array.from({ length: yTicks + 1 }, (_, i) => yLo + (i / yTicks) * (yHi - yLo));
  const n = points.length;

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" style={{ width: "100%", height: "100%", display: "block" }}>
      <rect x="0" y="0" width={W} height={H} fill="var(--chart-plot)" />
      {yTickVals.map((v, i) => (
        <g key={"y" + i}>
          <line x1={plotL} x2={plotR} y1={py(v)} y2={py(v)} stroke="var(--chart-grid)" strokeWidth="0.8" opacity="0.55" />
          <text x={plotL - 6} y={py(v) + 4} fill="var(--chart-axis)" textAnchor="end" style={labelFont}>{yFmt(v)}</text>
        </g>
      ))}
      {xTickVals.map((v, i) => (
        <text key={"x" + i} x={px(v)} y={H - 8} fill="var(--chart-axis)" textAnchor="middle" style={labelFont}>{xFmt(v)}</text>
      ))}

      {fit && (
        <line x1={px(xLo)} x2={px(xHi)} y1={py(fit.a + fit.b * xLo)} y2={py(fit.a + fit.b * xHi)}
          stroke="var(--accent)" strokeWidth="1.4" opacity="0.8" />
      )}

      {points.map((p, i) => {
        const op = fadeByRecency && n > 1 ? 0.16 + 0.58 * (i / (n - 1)) : 0.55;
        return <circle key={i} cx={px(p.x)} cy={py(p.y)} r="3" fill={pointColor} opacity={op} />;
      })}

      {current && (
        <circle cx={px(current.x)} cy={py(current.y)} r="5.5" fill={currentColor} stroke="var(--bg-light)" strokeWidth="1.5" />
      )}

      <line x1={plotL} x2={plotR} y1={plotB} y2={plotB} stroke="var(--chart-border)" strokeWidth="1.2" />
      <line x1={plotL} x2={plotL} y1={plotT} y2={plotB} stroke="var(--chart-border)" strokeWidth="1" opacity="0.5" />
    </svg>
  );
}

ScatterChart.displayName = "ScatterChart";
