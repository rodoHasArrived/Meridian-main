// Meridian DrawdownChart — the underwater plot. Drawdown (peak-to-trough decline, always ≤ 0)
// hangs below a 0% waterline as a filled curve, with an optional dashed limit rule and a marker
// on the maximum drawdown. Pairs beside an EquityCurve in backtest / strategy reporting.
import { niceTicks } from "./ticks";

export interface DrawdownChartProps {
  /** Drawdown series in percent — 0 or negative values (e.g. -12.4). */
  series: number[];
  /** Time-axis labels, parallel to `series`. */
  labels?: string[];
  /** Optional limit line in percent (e.g. -10) — dashed warning rule. */
  threshold?: number | null;
  /** Format for axis + marker values. @default v => `${v.toFixed(0)}%` */
  valueFmt?: (v: number) => string;
  /** Mark the maximum drawdown point. @default true */
  markMax?: boolean;
  /** @default 5 */
  valueTicks?: number;
  /** @default 7 */
  timeTicks?: number;
}

const PLOT = "var(--chart-plot, #FFFFFF)";
const GRID = "var(--chart-grid, #CBD3DC)";
const AXIS = "var(--chart-axis, #59636F)";
const BORDER = "var(--chart-border, #99A5B2)";
const DRAWDOWN = "var(--chart-drawdown, #BA3F55)";
const WARNING = "var(--chart-warning, #8A520E)";

export function DrawdownChart({
  series,
  labels = [],
  threshold = null,
  valueFmt = (v) => `${v.toFixed(0)}%`,
  markMax = true,
  valueTicks = 5,
  timeTicks = 7
}: DrawdownChartProps) {
  const W = 960;
  const H = 360;
  const padT = 14;
  const axisR = 56;
  const axisB = 26;
  const plotL = 8;
  const plotR = W - axisR;
  const plotT = padT;
  const plotB = H - axisB;

  const n = series.length;
  const denom = n > 1 ? n - 1 : 1;
  const dd = series.map((v) => Math.min(0, v));
  const min = Math.min(...dd, threshold != null ? threshold : 0, 0) * 1.08 || -1;
  const max = 0;

  const x = (i: number) => plotL + i * ((plotR - plotL) / denom);
  const y = (v: number) => plotT + (max - v) * ((plotB - plotT) / (max - min || 1));
  const path = dd.map((v, i) => `${i === 0 ? "M" : "L"}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(" ");
  const areaPath = n ? `M${x(0).toFixed(1)},${y(0).toFixed(1)} ${path.slice(1)} L${x(n - 1).toFixed(1)},${y(0).toFixed(1)} Z` : "";

  const vTicks = niceTicks(min, max, valueTicks).filter((v) => v <= 0);
  const tStep = Math.max(1, Math.round((n || 1) / timeTicks));
  const labelFont = { fontFamily: "var(--font-data, monospace)", fontSize: 11 };

  let maxIdx = 0;
  for (let i = 1; i < n; i++) if (dd[i] < dd[maxIdx]) maxIdx = i;

  return (
    <svg
      viewBox={`0 0 ${W} ${H}`}
      preserveAspectRatio="none"
      style={{ width: "100%", height: "100%", display: "block" }}
      role="img"
      aria-label="Drawdown underwater chart"
    >
      <rect x="0" y="0" width={W} height={H} fill={PLOT} />
      {vTicks.map((v, k) => (
        <g key={k}>
          <line x1={plotL} x2={plotR} y1={y(v)} y2={y(v)} stroke={GRID} strokeWidth="0.8" opacity="0.6" />
          <text x={plotR + 8} y={y(v) + 4} fill={AXIS} textAnchor="start" style={labelFont}>
            {valueFmt(v)}
          </text>
        </g>
      ))}
      {labels.map((lab, i) =>
        i % tStep === 0 || i === n - 1 ? (
          <text key={"t" + i} x={x(i)} y={H - 8} fill={AXIS} textAnchor="middle" style={labelFont}>
            {lab}
          </text>
        ) : null
      )}

      {/* underwater fill + curve */}
      {areaPath && <path d={areaPath} fill={DRAWDOWN} opacity="0.18" />}
      {path && <path d={path} fill="none" stroke={DRAWDOWN} strokeWidth="1.6" />}

      {/* 0% waterline */}
      <line x1={plotL} x2={plotR} y1={y(0)} y2={y(0)} stroke={BORDER} strokeWidth="1.2" />

      {/* threshold rule */}
      {threshold != null && (
        <g>
          <line x1={plotL} x2={plotR} y1={y(threshold)} y2={y(threshold)} stroke={WARNING} strokeWidth="1.2" strokeDasharray="5 4" opacity="0.85" />
          <text x={plotL + 4} y={y(threshold) - 5} fill={WARNING} style={{ ...labelFont, fontWeight: 600 }}>
            {valueFmt(threshold)} limit
          </text>
        </g>
      )}

      {/* max drawdown marker */}
      {markMax && n > 0 && (
        <g>
          <line x1={x(maxIdx)} x2={x(maxIdx)} y1={y(0)} y2={y(dd[maxIdx])} stroke={DRAWDOWN} strokeWidth="1" strokeDasharray="3 3" opacity="0.7" />
          <circle cx={x(maxIdx)} cy={y(dd[maxIdx])} r="3.5" fill={DRAWDOWN} />
          <rect x={Math.min(x(maxIdx) + 6, plotR - 70)} y={y(dd[maxIdx]) - 9} width="66" height="18" fill={DRAWDOWN} opacity="0.95" />
          <text x={Math.min(x(maxIdx) + 6, plotR - 70) + 33} y={y(dd[maxIdx]) + 4} fill="#fff" textAnchor="middle" style={{ ...labelFont, fontWeight: 600 }}>
            max {valueFmt(dd[maxIdx])}
          </text>
        </g>
      )}
      <line x1={plotR} x2={plotR} y1={plotT} y2={plotB} stroke={BORDER} strokeWidth="1.2" />
    </svg>
  );
}

DrawdownChart.displayName = "DrawdownChart";
