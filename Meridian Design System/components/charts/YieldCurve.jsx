// Meridian YieldCurve — term-structure line chart (yield vs tenor) for rates & bond
// surfaces (Basket Builder, fixed-income research). Tenors position on a sqrt-of-years
// x-scale so the money-market end doesn't crush; an optional second series (prior date,
// benchmark curve) overlays dashed; an optional tenor-pair spread readout ("2Y–10Y")
// prints top-left, red when inverted. Flat, token-driven, mirrors ScatterChart conventions.
import React from "react";

const X_SCALES = {
  sqrt: (y) => Math.sqrt(y),
  linear: (y) => y,
};

export function YieldCurve({
  points = [],
  compare = null,
  label = "Current",
  compareLabel = "Prior",
  yFmt = (v) => v.toFixed(2) + "%",
  yTicks = 5,
  xScale = "sqrt",
  spread = null,
  markers = true,
  lineColor = "var(--chart-primary)",
  compareColor = "var(--chart-secondary)",
}) {
  const W = 960, H = 340;
  const padT = 18, axisB = 26, axisL = 52, axisR = 16;
  const plotL = axisL, plotR = W - axisR, plotT = padT, plotB = H - axisB;

  const sc = typeof xScale === "function" ? xScale : X_SCALES[xScale] || X_SCALES.sqrt;
  const all = points.concat(compare || []);
  if (!all.length) return <svg viewBox={`0 0 ${W} ${H}`} style={{ width: "100%", height: "100%", display: "block" }} />;

  const xVals = all.map((p) => sc(p.years));
  const xLo = Math.min(...xVals), xHi = Math.max(...xVals);
  const yMin = Math.min(...all.map((p) => p.value));
  const yMax = Math.max(...all.map((p) => p.value));
  const yPad = (yMax - yMin || 1) * 0.14;
  const yLo = yMin - yPad, yHi = yMax + yPad;

  const px = (p) => plotL + ((sc(p.years) - xLo) / (xHi - xLo || 1)) * (plotR - plotL);
  const py = (v) => plotB - ((v - yLo) / (yHi - yLo || 1)) * (plotB - plotT);
  const path = (pts) => pts.map((p, i) => `${i ? "L" : "M"}${px(p).toFixed(1)},${py(p.value).toFixed(1)}`).join(" ");

  const labelFont = { fontFamily: "var(--font-data)", fontSize: 11 };
  const yTickVals = Array.from({ length: yTicks + 1 }, (_, i) => yLo + (i / yTicks) * (yHi - yLo));

  // spread readout: value(b) − value(a), in bp — negative (inversion) reads red
  let spreadNode = null;
  if (spread && spread.a && spread.b) {
    const pa = points.find((p) => p.tenor === spread.a);
    const pb = points.find((p) => p.tenor === spread.b);
    if (pa && pb) {
      const bp = Math.round((pb.value - pa.value) * 100);
      const neg = bp < 0;
      spreadNode = (
        <text x={plotL + 8} y={plotT + 12} style={labelFont}
          fill={neg ? "var(--red)" : "var(--text-secondary)"}>
          {`${spread.a}–${spread.b} ${bp >= 0 ? "+" : "−"}${Math.abs(bp)}bp${neg ? " · inverted" : ""}`}
        </text>
      );
    }
  }

  const series = (pts, color, dashed, r) => (
    <g>
      <path d={path(pts)} fill="none" stroke={color} strokeWidth="1.6"
        strokeDasharray={dashed ? "5 4" : undefined} />
      {markers && pts.map((p, i) => (
        <circle key={i} cx={px(p)} cy={py(p.value)} r={r} fill={color} stroke="var(--chart-plot)" strokeWidth="1.2" />
      ))}
    </g>
  );

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" style={{ width: "100%", height: "100%", display: "block" }}
      role="img" aria-label={`Yield curve, ${points.length} tenors`}>
      <rect x="0" y="0" width={W} height={H} fill="var(--chart-plot)" />
      {yTickVals.map((v, i) => (
        <g key={"y" + i}>
          <line x1={plotL} x2={plotR} y1={py(v)} y2={py(v)} stroke="var(--chart-grid)" strokeWidth="0.8" opacity="0.55" />
          <text x={plotL - 6} y={py(v) + 4} fill="var(--chart-axis)" textAnchor="end" style={labelFont}>{yFmt(v)}</text>
        </g>
      ))}
      {points.map((p, i) => (
        <g key={"x" + i}>
          <line x1={px(p)} x2={px(p)} y1={plotB} y2={plotB + 4} stroke="var(--chart-border)" strokeWidth="1" />
          <text x={px(p)} y={H - 8} fill="var(--chart-axis)" textAnchor="middle" style={labelFont}>{p.tenor}</text>
        </g>
      ))}

      {compare && series(compare, compareColor, true, 2.4)}
      {series(points, lineColor, false, 3.2)}
      {spreadNode}

      {compare && (
        <g>
          <line x1={plotR - 150} x2={plotR - 128} y1={plotT + 8} y2={plotT + 8} stroke={lineColor} strokeWidth="1.6" />
          <text x={plotR - 122} y={plotT + 12} fill="var(--text-secondary)" style={labelFont}>{label}</text>
          <line x1={plotR - 150} x2={plotR - 128} y1={plotT + 24} y2={plotT + 24} stroke={compareColor} strokeWidth="1.6" strokeDasharray="5 4" />
          <text x={plotR - 122} y={plotT + 28} fill="var(--text-secondary)" style={labelFont}>{compareLabel}</text>
        </g>
      )}

      <line x1={plotL} x2={plotR} y1={plotB} y2={plotB} stroke="var(--chart-border)" strokeWidth="1.2" />
      <line x1={plotL} x2={plotL} y1={plotT} y2={plotB} stroke="var(--chart-border)" strokeWidth="1" opacity="0.5" />
    </svg>
  );
}

YieldCurve.displayName = "YieldCurve";
