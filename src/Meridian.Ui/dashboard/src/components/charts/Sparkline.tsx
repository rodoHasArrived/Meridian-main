// Meridian Sparkline — inline mini chart for MetricCard and table cells.
// Line, area, and bar variants. SVG-based, zero deps. Concrete tokens with hex fallbacks.

export interface SparklineProps {
  /** The data series. An empty array renders nothing. */
  points?: number[];
  /** @default 80 */
  width?: number;
  /** @default 28 */
  height?: number;
  /** @default "line" */
  variant?: "line" | "area" | "bar";
  /** Series color. @default var(--chart-equity, #16885F) */
  color?: string;
  /** Line stroke width. @default 1.5 */
  strokeWidth?: number;
  /** Reference line / bar-sign pivot (e.g. 0 for P&L). Values below it draw red. */
  baseline?: number | null;
}

const DOWN = "var(--chart-drawdown, #BA3F55)";

export function Sparkline({
  points = [],
  width = 80,
  height = 28,
  variant = "line",
  color = "var(--chart-equity, #16885F)",
  strokeWidth = 1.5,
  baseline = null
}: SparklineProps) {
  if (!points.length) return null;

  const n = points.length;
  const min = Math.min(...points);
  const max = Math.max(...points);
  const range = max - min || 1;
  const padV = 2;

  const xScale = (i: number) => (n === 1 ? width / 2 : (i / (n - 1)) * width);
  const yScale = (v: number) => height - padV - ((v - min) / range) * (height - padV * 2);

  if (variant === "bar") {
    const barW = Math.max(1, (width / n) * 0.72);
    return (
      <svg
        width={width}
        height={height}
        viewBox={`0 0 ${width} ${height}`}
        style={{ display: "block", overflow: "visible" }}
        aria-hidden="true"
      >
        {points.map((v, i) => {
          const barH = Math.max(1, ((v - min) / range) * (height - padV));
          const isPos = baseline == null || v >= baseline;
          return (
            <rect
              key={i}
              x={xScale(i) - barW / 2}
              y={height - barH}
              width={barW}
              height={barH}
              fill={isPos ? color : DOWN}
              opacity="0.85"
            />
          );
        })}
      </svg>
    );
  }

  const pathD = points.map((v, i) => `${i === 0 ? "M" : "L"}${xScale(i).toFixed(1)},${yScale(v).toFixed(1)}`).join(" ");
  const areaD = `${pathD} L${xScale(n - 1).toFixed(1)},${height} L${xScale(0).toFixed(1)},${height} Z`;

  return (
    <svg
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      style={{ display: "block", overflow: "visible" }}
      aria-hidden="true"
    >
      {variant === "area" && <path d={areaD} fill={color} opacity="0.15" />}
      {baseline != null && (
        <line
          x1={0}
          x2={width}
          y1={yScale(baseline)}
          y2={yScale(baseline)}
          stroke={color}
          strokeWidth="0.5"
          opacity="0.4"
          strokeDasharray="2 2"
        />
      )}
      <path d={pathD} fill="none" stroke={color} strokeWidth={strokeWidth} strokeLinecap="round" strokeLinejoin="round" />
      <circle cx={xScale(n - 1)} cy={yScale(points[n - 1])} r="2.5" fill={color} />
    </svg>
  );
}

Sparkline.displayName = "Sparkline";
