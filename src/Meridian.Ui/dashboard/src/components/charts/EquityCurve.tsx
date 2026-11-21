// Meridian equity curve — line (with area fill) + benchmark overlay, value-axis labels,
// gridlines, legend, crosshair readout, and an optional drawdown subpane. Mirrors the
// performance charts in analytics / backtest reporting. Flex-fills its container and draws at its
// measured pixel size, so axis text is never stretched (UX Phase F); the value axis sits on the left.
import { AXIS_LABEL_STYLE, fitValueTicks, formatTimeTickLabel, selectTimeTickIndices, timeLabelSpanDays } from "./axis-ticks";
import { ChartSvg, type ChartA11yProps } from "./chart-a11y";
import { ChartInteractionOverlay, isChartInteractive } from "./chart-interaction";
import { useChartSize } from "./use-chart-size";
import { ChartEvidenceMark, type ChartInspection } from "./chart-evidence";

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

export interface EquityCurveProps extends ChartA11yProps {
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
  /**
   * Emit the hovered/keyboard-focused point index (null on leave). Providing this
   * (or `onPointActivate`) enables an interactive overlay over the plot area.
   */
  onCrosshairChange?: (index: number | null) => void;
  /** Emit the activated point index (click, Enter, or Space). */
  onPointActivate?: (index: number) => void;
  /** Approx. number of value gridlines. @default 6 */
  valueTicks?: number;
  /** Approx. number of time labels. @default 7 */
  timeTicks?: number;
  /** Show the legend row. @default true */
  showLegend?: boolean;
  /** Area fill under the primary series. @default true */
  fill?: boolean;
  inspection?: ChartInspection;
}

const PLOT = "var(--chart-plot, #F8F7F3)";
const GRID = "var(--chart-grid, #E2DED5)";
const AXIS = "var(--chart-axis, #606870)";
const BORDER = "var(--chart-border, #AAA49A)";
const CROSSHAIR = "var(--chart-crosshair, #C56343)";
const DRAWDOWN = "var(--chart-drawdown, #B54B42)";

export function EquityCurve({
  series,
  labels = [],
  drawdown = null,
  valueFmt = (v) => v.toFixed(0),
  crosshairIndex = null,
  onCrosshairChange,
  onPointActivate,
  valueTicks = 6,
  timeTicks = 7,
  showLegend = true,
  fill = true,
  ariaLabel = "Equity performance curve",
  ariaDescription,
  accessibleData,
  inspection
}: EquityCurveProps) {
  const [plotRef, size] = useChartSize({ width: 960, height: 460 });
  const W = size.width;
  const H = size.height;
  const axisL = 64;
  const padT = 14;
  const padR = 12;
  const axisB = 28;
  const ddH = drawdown ? 88 : 0;
  const ddGap = drawdown ? 12 : 0;
  const plotL = axisL;
  const plotR = W - padR;
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
  const line = (pts: number[]) => pts.length === 0 ? "" : pts.map((v, i) => `${i === 0 ? "M" : "L"}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(" ");
  const area = (pts: number[]) => pts.length === 0 ? "" : `${line(pts)} L${x(n - 1).toFixed(1)},${eqB} L${x(0).toFixed(1)},${eqB} Z`;

  const vTicks = fitValueTicks(min, max, valueTicks, eqB - eqT);
  const timeTickIndices = new Set(selectTimeTickIndices(Math.min(n, labels.length), plotR - plotL, timeTicks));
  const spanDays = timeLabelSpanDays(labels.slice(0, n));
  const axFont = { ...AXIS_LABEL_STYLE, fontSize: 12, fontWeight: 500 };
  const labelFont = AXIS_LABEL_STYLE;

  // drawdown subpane scale (drawdown values are <= 0)
  const ddMin = drawdown ? Math.min(...drawdown, 0) : 0;
  const ddSpan = Math.abs(ddMin) || 1;
  const yD = (v: number) => ddT + Math.abs(v) * ((ddB - ddT) / ddSpan);
  const hasDrawdown = !!(drawdown && drawdown.length > 0);
  const ddArea = hasDrawdown
    ? `M${x(0).toFixed(1)},${yD(0).toFixed(1)} ${drawdown
        .map((v, i) => `L${x(i).toFixed(1)},${yD(v).toFixed(1)}`)
        .join(" ")} L${x(n - 1).toFixed(1)},${yD(0).toFixed(1)} Z`
    : "";

  const legendText = { fontFamily: AXIS_LABEL_STYLE.fontFamily, fontSize: 12, color: "var(--text-secondary, #535C65)" };

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
      <div ref={plotRef} style={{ flex: 1, minHeight: 0, display: "flex", flexDirection: "column" }}>
      <ChartSvg
        viewBox={`0 0 ${W} ${H}`}
        style={{ width: "100%", flex: 1, minHeight: 0, display: "block" }}
        ariaLabel={ariaLabel}
        ariaDescription={ariaDescription}
        accessibleData={accessibleData}
        overlayHintId={inspection?.hintId}
        overlay={
          inspection ? primary?.points.map((_, index) => {
            const halfSlot = (plotR - plotL) / denom / 2;
            const left = Math.max(plotL, x(index) - halfSlot);
            const right = Math.min(plotR, x(index) + halfSlot);
            return <ChartEvidenceMark key={inspection.pointId(index)} inspection={inspection} index={index} geometry={{ x: left, y: eqT, width: right - left, height: ddB - eqT }} />;
          }) : isChartInteractive(n, onCrosshairChange, onPointActivate) ? (
            <ChartInteractionOverlay
              x={plotL}
              y={eqT}
              width={plotR - plotL}
              height={ddB - eqT}
              count={n}
              crosshairIndex={crosshairIndex}
              onCrosshairChange={onCrosshairChange}
              onPointActivate={onPointActivate}
              ariaLabel="Move crosshair across equity points; activate to drill into the selected point"
            />
          ) : null
        }
      >
        <rect x="0" y="0" width={W} height={H} fill={PLOT} />
        {/* value gridlines + labels */}
        {vTicks.map((v, k) => (
          <g key={k}>
            <line x1={plotL} x2={plotR} y1={y(v)} y2={y(v)} stroke={GRID} strokeWidth="0.8" opacity="0.65" />
            <text x={plotL - 8} y={y(v) + 4} fill={AXIS} textAnchor="end" style={labelFont}>
              {valueFmt(v)}
            </text>
          </g>
        ))}
        {/* time labels */}
        {labels.map((lab, i) =>
          timeTickIndices.has(i) ? (
            <g key={"t" + i}>
              <line x1={x(i)} x2={x(i)} y1={eqT} y2={eqB} stroke={GRID} strokeWidth="0.8" opacity="0.55" />
              <text x={x(i)} y={H - 8} fill={AXIS} textAnchor={i === 0 ? "start" : i === n - 1 ? "end" : "middle"} style={labelFont}>
                {formatTimeTickLabel(lab, spanDays)}
              </text>
            </g>
          ) : null
        )}
        <line x1={plotL} x2={plotL} y1={eqT} y2={ddB} stroke={BORDER} strokeWidth="1.2" />
        <line x1={plotL} x2={plotR} y1={eqB} y2={eqB} stroke={BORDER} strokeWidth="1.2" />

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
        {hasDrawdown && (
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
            <text x={plotL - 8} y={yD(0) + 4} fill={AXIS} textAnchor="end" style={labelFont}>
              0%
            </text>
            <text x={plotL - 8} y={ddB} fill={AXIS} textAnchor="end" style={labelFont}>
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
            <rect x={2} y={y(primary.points[crosshairIndex]) - 10} width={axisL - 6} height="20" fill={CROSSHAIR} rx="2" opacity="0.95" />
            <text
              x={(axisL - 2) / 2}
              y={y(primary.points[crosshairIndex]) + 5}
              fill="#fff"
              textAnchor="middle"
              style={{ ...axFont, fontWeight: 600, fontSize: 13 }}
            >
              {valueFmt(primary.points[crosshairIndex])}
            </text>
          </g>
        )}
      </ChartSvg>
      </div>
    </div>
  );
}

EquityCurve.displayName = "EquityCurve";
