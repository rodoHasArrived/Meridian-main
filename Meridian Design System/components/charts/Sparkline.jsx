// Meridian Sparkline — inline mini chart for MetricCard and table cells.
// Supports line, area, and bar variants. SVG-based, zero deps.
import React from "react";

export function Sparkline({
  points = [],
  width = 80,
  height = 28,
  variant = "line", // "line" | "area" | "bar"
  color = "var(--chart-equity,#16885F)",
  strokeWidth = 1.5,
  baseline = null, // reference line (e.g. 0 for P&L)
}) {
  if (!points.length) return null;

  const n = points.length;
  const min = Math.min(...points);
  const max = Math.max(...points);
  const range = max - min || 1;
  const padV = 2;

  const xScale = (i) => (i / (n - 1)) * width;
  const yScale = (v) => height - padV - ((v - min) / range) * (height - padV * 2);

  const pathD = points.map((v, i) =>
    `${i === 0 ? "M" : "L"}${xScale(i).toFixed(1)},${yScale(v).toFixed(1)}`
  ).join(" ");

  const areaD = `${pathD} L${xScale(n - 1).toFixed(1)},${height} L0,${height} Z`;

  if (variant === "bar") {
    const barW = Math.max(1, (width / n) * 0.72);
    return (
      <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`}
        style={{ display:"block", overflow:"visible" }}>
        {points.map((v, i) => {
          const barH = Math.max(1, ((v - min) / range) * (height - padV));
          const isPos = baseline == null || v >= baseline;
          return (
            <rect key={i}
              x={xScale(i) - barW / 2} y={height - barH}
              width={barW} height={barH}
              fill={isPos ? color : "var(--chart-drawdown,#BA3F55)"}
              opacity="0.85"
            />
          );
        })}
      </svg>
    );
  }

  return (
    <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`}
      style={{ display:"block", overflow:"visible" }}>
      {variant === "area" && (
        <path d={areaD} fill={color} opacity="0.15" />
      )}
      {baseline != null && (
        <line x1={0} x2={width} y1={yScale(baseline)} y2={yScale(baseline)}
          stroke={color} strokeWidth="0.5" opacity="0.4" strokeDasharray="2 2" />
      )}
      <path d={pathD} fill="none" stroke={color} strokeWidth={strokeWidth}
        strokeLinecap="round" strokeLinejoin="round" />
      <circle cx={xScale(n - 1)} cy={yScale(points[n - 1])} r="2.5" fill={color} />
    </svg>
  );
}
