// Meridian equity curve — line (with area fill) + benchmark overlay, value-axis labels,
// gridlines, legend, crosshair readout, and an optional drawdown subpane. Mirrors the
// performance charts in analytics / backtest reporting. Flex-fills its container.
import { niceTicks } from "./ticks";

export interface EquitySeries {
  label: string;
  /** CSS color (token var). */
  color: string;
  points: number[];
  /** Render dashed (benchmarks). @default false */
  dashed?: boolean;
  /** Set false to skip the area fill on the primary series. */
  area?: boolean;
}

export interface EquityCurveProps {
  /** First series is primary (gets the area fill + crosshair price chip). */
  series: EquitySeries[];
  /** X-axis time labels, aligned to the point index. */
  labels?: string[];
  /** Drawdown values (≤ 0) for the bottom subpane. */
  drawdown?: number[] | null;
  /** Format value-axis + crosshair labels. @default v => v.toFixed(0) */
  valueFmt?: (v: number) => string;
  /** Point index to mark with the crosshair. */
  crosshairIndex?: number | null;
  /** Approx. number of value gridlines. @default 6 */
  valueTicks?: number;
  /** Approx. number of time labels. @default 7 */
  timeTicks?: number;
  /** Show the legend row. @default true */
  showLegend?: boolean;
  /** Area fill under the primary series. @default true */
  fill?: boolean;
}

const PLOT = "var(--chart-plot, #FFFFFF)";
const GRID = "var(--chart-grid, #CBD3DC)";
const AXIS = "var(--chart-axis, #59636F)";
const BORDER = "var(--chart-border, #99A5B2)";
const CROSSHAIR = "var(--chart-crosshair, #2F6F8F)";
const DRAWDOWN = "var(--chart-drawdown, #BA3F55)";

export function EquityCurve({
  series,
  labels = [],
  drawdown = null,
  valueFmt = (v) => v.toFixed(0),
  crosshairIndex = null,
  valueTicks = 6,
  timeTicks = 7,
  showLegend = true,
  fill = true
}: EquityCurveProps) {
  const W = 960;
  const H = 460;
  const padL = 48;
  const padT = 14;
  const axisR = 62;
  const axisB = 28;
  const ddH = drawdown ? 88 : 0;
  const ddGap = drawdown ? 12 : 0;
  const plotL = padL;
  const plotR = W - axisR;
  const eqT = padT;
  const eqB = H - axisB - ddH - ddGap;
  const ddT = eqB + ddGap;
  const ddB = H - axisB;

  const primary = series[0];
  const n = primary ? primary.points.length : 0;
  const denom = n > 1 ? n - 1 : 1;
  const allV = series.flatMap((s) => s.points);
  let max = allV.length ? Math.max(...allV) : 1;
  let min = allV.length ? Math.min(...allV) : 0;
  const pv = (max - min) * 0.08 || 1;
  max += pv;
  min -= pv;

  const x = (i: number) => plotL + i * ((plotR - plotL) / denom);
  const y = (v: number) => eqT + (max - v) * ((eqB - eqT) / (max - min || 1));
  const line = (pts: number[]) => pts.map((v, i) => `${i === 0 ? "M" : "L"}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(" ");
  const area = (pts: number[]) => `${line(pts)} L${x(n - 1).toFixed(1)},${eqB} L${x(0).toFixed(1)},${eqB} Z`;

  const vTicks = niceTicks(min, max, valueTicks);
  const tStep = Math.max(1, Math.round((n || 1) / timeTicks));
  const axFont = { fontFamily: "var(--font-data, monospace)", fontSize: 12, fontWeight: 500 };
  const labelFont = { fontFamily: "var(--font-data, monospace)", fontSize: 11 };

  // drawdown subpane scale (drawdown values are <= 0)
  const ddMin = drawdown ? Math.min(...drawdown, 0) : 0;
  const yD = (v: number) => ddT + (0 - v) * ((ddB - ddT) / (ddMin || -1));
  const ddArea = drawdown
    ? `M${x(0).toFixed(1)},${yD(0).toFixed(1)} ${drawdown
        .map((v, i) => `L${x(i).toFixed(1)},${yD(v).toFixed(1)}`)
        .join(" ")} L${x(n - 1).toFixed(1)},${yD(0).toFixed(1)} Z`
    : "";

  const legendText = { fontFamily: "var(--font-data, monospace)", fontSize: 11, color: "var(--text-secondary, #4D5967)" };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
      {showLegend && (
        <div style={{ display: "flex", gap: 16, padding: "0 8px 8px", flexWrap: "wrap" }}>
          {series.map((s, k) => (
            <span key={k} style={{ display: "inline-flex", alignItems: "center", gap: 6, ...legendText }}>
              <span style={{ width: 14, height: 0, borderTop: `2px ${s.dashed ? "dashed" : "solid"} ${s.color}` }} />
              {s.label}
            </span>
          ))}
          {drawdown && (
            <span style={{ display: "inline-flex", alignItems: "center", gap: 6, ...legendText }}>
              <span style={{ width: 14, height: 8, background: DRAWDOWN, opacity: 0.25 }} />
              drawdown
            </span>
          )}
        </div>
      )}
      <svg
        viewBox={`0 0 ${W} ${H}`}
        preserveAspectRatio="none"
        style={{ width: "100%", flex: 1, display: "block" }}
        role="img"
        aria-label="Equity performance curve"
      >
        <rect x="0" y="0" width={W} height={H} fill={PLOT} />
        {/* value gridlines + labels */}
        {vTicks.map((v, k) => (
          <g key={k}>
            <line x1={plotL} x2={plotR} y1={y(v)} y2={y(v)} stroke={GRID} strokeWidth="0.8" opacity="0.65" />
            <text x={plotR + 8} y={y(v) + 5} fill={AXIS} textAnchor="start" style={labelFont}>
              {valueFmt(v)}
            </text>
          </g>
        ))}
        {/* time labels */}
        {labels.map((lab, i) =>
          i % tStep === 0 || i === n - 1 ? (
            <g key={"t" + i}>
              <line x1={x(i)} x2={x(i)} y1={eqT} y2={eqB} stroke={GRID} strokeWidth="0.8" opacity="0.55" />
              <text x={x(i)} y={H - 8} fill={AXIS} textAnchor="middle" style={labelFont}>
                {lab}
              </text>
            </g>
          ) : null
        )}
        <line x1={plotR} x2={plotR} y1={eqT} y2={ddB} stroke={BORDER} strokeWidth="1.2" />
        <line x1={plotL - 1} x2={plotR} y1={eqB} y2={eqB} stroke={BORDER} strokeWidth="1.2" />
        <line x1={plotL - 1} x2={plotL - 1} y1={eqT} y2={eqB} stroke={BORDER} strokeWidth="1" opacity="0.4" />

        {/* area fill for the primary series */}
        {fill && primary && primary.area !== false && <path d={area(primary.points)} fill={primary.color} opacity="0.10" />}
        {/* series lines */}
        {series.map((s, k) => (
          <path
            key={k}
            d={line(s.points)}
            fill="none"
            stroke={s.color}
            strokeWidth={k === 0 ? 2 : 1.5}
            strokeDasharray={s.dashed ? "5 4" : undefined}
          />
        ))}

        {/* drawdown subpane */}
        {drawdown && (
          <g>
            <path d={ddArea} fill={DRAWDOWN} opacity="0.20" />
            <path
              d={`M${x(0).toFixed(1)},${yD(drawdown[0]).toFixed(1)} ${drawdown
                .map((v, i) => `L${x(i).toFixed(1)},${yD(v).toFixed(1)}`)
                .join(" ")}`}
              fill="none"
              stroke={DRAWDOWN}
              strokeWidth="1.4"
            />
            <line x1={plotL - 1} x2={plotR} y1={yD(0)} y2={yD(0)} stroke={BORDER} strokeWidth="1" opacity="0.6" />
            <text x={plotR + 8} y={yD(0) + 5} fill={AXIS} textAnchor="start" style={labelFont}>
              0%
            </text>
            <text x={plotR + 8} y={ddB} fill={AXIS} textAnchor="start" style={labelFont}>
              {ddMin.toFixed(0)}%
            </text>
          </g>
        )}

        {/* crosshair */}
        {crosshairIndex != null && primary && primary.points[crosshairIndex] != null && (
          <g>
            <line
              x1={x(crosshairIndex)}
              x2={x(crosshairIndex)}
              y1={eqT}
              y2={ddB}
              stroke={CROSSHAIR}
              strokeWidth="1.2"
              strokeDasharray="4 2"
              opacity="0.8"
            />
            {series.map((s, k) => (
              <circle key={k} cx={x(crosshairIndex)} cy={y(s.points[crosshairIndex])} r="4" fill={s.color} opacity="0.9" />
            ))}
            <rect x={plotR + 2} y={y(primary.points[crosshairIndex]) - 10} width={axisR - 4} height="20" fill={CROSSHAIR} rx="2" opacity="0.95" />
            <text
              x={plotR + axisR / 2}
              y={y(primary.points[crosshairIndex]) + 5}
              fill="#fff"
              textAnchor="middle"
              style={{ ...axFont, fontWeight: 600, fontSize: 13 }}
            >
              {valueFmt(primary.points[crosshairIndex])}
            </text>
          </g>
        )}
      </svg>
    </div>
  );
}

EquityCurve.displayName = "EquityCurve";
