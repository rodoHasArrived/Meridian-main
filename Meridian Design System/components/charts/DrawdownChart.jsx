// Meridian DrawdownChart — the underwater plot. Drawdown is peak-to-trough decline, always
// ≤ 0; the curve hangs below a 0% waterline with a filled area, an optional recovery threshold
// rule, and a marker on the maximum drawdown. Pair beside an EquityCurve in backtest reports.
import React from "react";

function niceTicks(min, max, count) {
  const span = max - min || 1;
  const raw = span / count;
  const mag = Math.pow(10, Math.floor(Math.log10(raw)));
  const norm = raw / mag;
  const step = (norm >= 5 ? 5 : norm >= 2 ? 2 : 1) * mag;
  const start = Math.ceil(min / step) * step;
  const out = [];
  for (let v = start; v <= max + 1e-9; v += step) out.push(+v.toFixed(6));
  return out;
}

export function DrawdownChart({
  series,                       // number[] of drawdown %, 0 or negative (e.g. -12.4)
  labels = [],
  threshold = null,            // e.g. -10 → dashed warning rule + red fill below it
  valueFmt = (v) => `${v.toFixed(0)}%`,
  markMax = true,
  valueTicks = 5,
  timeTicks = 7,
}) {
  const W = 960, H = 360;
  const padT = 14, axisR = 56, axisB = 26, plotL = 8;
  const plotR = W - axisR, plotT = padT, plotB = H - axisB;

  const n = series.length;
  const dd = series.map((v) => Math.min(0, v));
  const min = Math.min(...dd, threshold != null ? threshold : 0) * 1.08;
  const max = 0;

  const x = (i) => plotL + i * ((plotR - plotL) / (n - 1));
  const y = (v) => plotT + (max - v) * ((plotB - plotT) / (max - min));
  const path = dd.map((v, i) => `${i === 0 ? "M" : "L"}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(" ");
  const areaPath = `M${x(0).toFixed(1)},${y(0).toFixed(1)} ${path.slice(1)} L${x(n - 1).toFixed(1)},${y(0).toFixed(1)} Z`;

  const vTicks = niceTicks(min, max, valueTicks).filter((v) => v <= 0);
  const tStep = Math.max(1, Math.round(n / timeTicks));
  const labelFont = { fontFamily: "var(--font-data)", fontSize: 11 };

  let maxIdx = 0;
  for (let i = 1; i < n; i++) if (dd[i] < dd[maxIdx]) maxIdx = i;

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" style={{ width: "100%", height: "100%", display: "block" }}>
      <rect x="0" y="0" width={W} height={H} fill="var(--chart-plot)" />
      {vTicks.map((v, k) => (
        <g key={k}>
          <line x1={plotL} x2={plotR} y1={y(v)} y2={y(v)} stroke="var(--chart-grid)" strokeWidth="0.8" opacity="0.6" />
          <text x={plotR + 8} y={y(v) + 4} fill="var(--chart-axis)" textAnchor="start" style={labelFont}>{valueFmt(v)}</text>
        </g>
      ))}
      {labels.map((lab, i) => (i % tStep === 0 || i === n - 1) ? (
        <text key={"t" + i} x={x(i)} y={H - 8} fill="var(--chart-axis)" textAnchor="middle" style={labelFont}>{lab}</text>
      ) : null)}

      {/* underwater fill + curve */}
      <path d={areaPath} fill="var(--chart-drawdown)" opacity="0.18" />
      <path d={path} fill="none" stroke="var(--chart-drawdown)" strokeWidth="1.6" />

      {/* 0% waterline */}
      <line x1={plotL} x2={plotR} y1={y(0)} y2={y(0)} stroke="var(--chart-border)" strokeWidth="1.2" />

      {/* threshold rule */}
      {threshold != null && (
        <g>
          <line x1={plotL} x2={plotR} y1={y(threshold)} y2={y(threshold)} stroke="var(--chart-warning)" strokeWidth="1.2" strokeDasharray="5 4" opacity="0.85" />
          <text x={plotL + 4} y={y(threshold) - 5} fill="var(--chart-warning)" style={{ ...labelFont, fontWeight: 600 }}>{valueFmt(threshold)} limit</text>
        </g>
      )}

      {/* max drawdown marker */}
      {markMax && n > 0 && (
        <g>
          <line x1={x(maxIdx)} x2={x(maxIdx)} y1={y(0)} y2={y(dd[maxIdx])} stroke="var(--chart-drawdown)" strokeWidth="1" strokeDasharray="3 3" opacity="0.7" />
          <circle cx={x(maxIdx)} cy={y(dd[maxIdx])} r="3.5" fill="var(--chart-drawdown)" />
          <rect x={Math.min(x(maxIdx) + 6, plotR - 70)} y={y(dd[maxIdx]) - 9} width="66" height="18" fill="var(--chart-drawdown)" opacity="0.95" />
          <text x={Math.min(x(maxIdx) + 6, plotR - 70) + 33} y={y(dd[maxIdx]) + 4} fill="#fff" textAnchor="middle" style={{ ...labelFont, fontWeight: 600 }}>max {valueFmt(dd[maxIdx])}</text>
        </g>
      )}
      <line x1={plotR} x2={plotR} y1={plotT} y2={plotB} stroke="var(--chart-border)" strokeWidth="1.2" />
    </svg>
  );
}

DrawdownChart.displayName = "DrawdownChart";
