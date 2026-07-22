// Meridian Histogram — distribution of a sample (daily returns, slippage, fill latency). Pass
// raw `values` (auto-binned) or precomputed `bins`. When `signed`, bars left of zero tint red and
// right of zero green so a returns distribution reads instantly; an optional dashed mean rule
// overlays. Flat, token-driven, explicit-height svg.

export interface HistogramBin {
  x0: number;
  x1: number;
  count: number;
}

export interface HistogramProps {
  /** Raw sample — auto-binned. Provide this OR `bins`. */
  values?: number[] | null;
  /** Precomputed bins. Provide this OR `values`. */
  bins?: HistogramBin[] | null;
  /** Bin count when auto-binning `values`. @default 24 */
  binCount?: number;
  /** Tint bars red/green by the sign of their center (returns view). @default true */
  signed?: boolean;
  /** Draw the dashed mean rule. @default true */
  showMean?: boolean;
  /** Override the computed mean. */
  mean?: number | null;
  /** Format for x-axis bin-edge values. @default v => v.toFixed(1) */
  valueFmt?: (v: number) => string;
  /** Format for y-axis counts. @default String */
  countFmt?: (c: number) => string;
  /** @default 7 */
  xTicks?: number;
}

const PLOT = "var(--chart-plot, #FFFFFF)";
const GRID = "var(--chart-grid, #CBD3DC)";
const AXIS = "var(--chart-axis, #59636F)";
const BORDER = "var(--chart-border, #99A5B2)";
const CROSSHAIR = "var(--chart-crosshair, #2F6F8F)";
const UP = "var(--chart-equity, #16885F)";
const DOWN = "var(--chart-drawdown, #BA3F55)";
const PRIMARY = "var(--chart-primary, #2F6F8F)";

function buildBins(values: number[], binCount: number): HistogramBin[] {
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max - min || 1;
  const w = span / binCount;
  const bins: HistogramBin[] = Array.from({ length: binCount }, (_, i) => ({
    x0: min + i * w,
    x1: min + (i + 1) * w,
    count: 0
  }));
  for (const v of values) {
    let i = Math.floor((v - min) / w);
    if (i >= binCount) i = binCount - 1;
    if (i < 0) i = 0;
    bins[i].count += 1;
  }
  return bins;
}

export function Histogram({
  values = null,
  bins: presetBins = null,
  binCount = 24,
  signed = true,
  showMean = true,
  mean: meanProp = null,
  valueFmt = (v) => v.toFixed(1),
  countFmt = (c) => String(c),
  xTicks = 7
}: HistogramProps) {
  const bins = presetBins || (values && values.length ? buildBins(values, binCount) : []);
  const W = 960;
  const H = 340;
  const padT = 14;
  const axisB = 28;
  const axisL = 40;
  const axisR = 10;
  const plotL = axisL;
  const plotR = W - axisR;
  const plotT = padT;
  const plotB = H - axisB;
  const n = bins.length;
  const maxCount = Math.max(1, ...bins.map((b) => b.count));
  const lo = n ? bins[0].x0 : 0;
  const hi = n ? bins[n - 1].x1 : 1;
  const span = hi - lo || 1;

  const bx = (v: number) => plotL + ((v - lo) / span) * (plotR - plotL);
  const by = (c: number) => plotB - (c / maxCount) * (plotB - plotT);
  const gap = 1.5;

  const mean =
    meanProp != null ? meanProp : values && values.length ? values.reduce((a, b) => a + b, 0) / values.length : null;

  const cTicks = [0, 0.25, 0.5, 0.75, 1].map((f) => Math.round(f * maxCount));
  const labelFont = { fontFamily: "var(--font-data, monospace)", fontSize: 11 };
  const tStep = Math.max(1, Math.round((n || 1) / xTicks));

  return (
    <svg
      viewBox={`0 0 ${W} ${H}`}
      preserveAspectRatio="none"
      style={{ width: "100%", height: "100%", display: "block" }}
      role="img"
      aria-label="Distribution histogram"
    >
      <rect x="0" y="0" width={W} height={H} fill={PLOT} />
      {[...new Set(cTicks)].map((c, k) => (
        <g key={k}>
          <line x1={plotL} x2={plotR} y1={by(c)} y2={by(c)} stroke={GRID} strokeWidth="0.8" opacity="0.55" />
          <text x={plotL - 6} y={by(c) + 4} fill={AXIS} textAnchor="end" style={labelFont}>
            {countFmt(c)}
          </text>
        </g>
      ))}

      {bins.map((b, i) => {
        const center = (b.x0 + b.x1) / 2;
        const color = !signed ? PRIMARY : center < 0 ? DOWN : UP;
        const x0 = bx(b.x0) + gap;
        const x1 = bx(b.x1) - gap;
        const top = by(b.count);
        return <rect key={i} x={x0} y={top} width={Math.max(0.5, x1 - x0)} height={plotB - top} fill={color} opacity="0.75" />;
      })}

      {/* x labels at bin edges */}
      {bins.map((b, i) =>
        i % tStep === 0 ? (
          <text key={"x" + i} x={bx(b.x0)} y={H - 9} fill={AXIS} textAnchor="middle" style={labelFont}>
            {valueFmt(b.x0)}
          </text>
        ) : null
      )}
      {n > 0 && (
        <text x={bx(bins[n - 1].x1)} y={H - 9} fill={AXIS} textAnchor="middle" style={labelFont}>
          {valueFmt(bins[n - 1].x1)}
        </text>
      )}

      {/* zero reference */}
      {signed && lo < 0 && hi > 0 && <line x1={bx(0)} x2={bx(0)} y1={plotT} y2={plotB} stroke={BORDER} strokeWidth="1" opacity="0.5" />}

      {/* mean rule */}
      {showMean && mean != null && mean >= lo && mean <= hi && (
        <g>
          <line x1={bx(mean)} x2={bx(mean)} y1={plotT} y2={plotB} stroke={CROSSHAIR} strokeWidth="1.2" strokeDasharray="5 3" />
          <rect x={Math.min(bx(mean) + 5, plotR - 78)} y={plotT} width="74" height="17" fill={CROSSHAIR} opacity="0.95" />
          <text x={Math.min(bx(mean) + 5, plotR - 78) + 37} y={plotT + 12} fill="#fff" textAnchor="middle" style={{ ...labelFont, fontWeight: 600 }}>
            μ {valueFmt(mean)}
          </text>
        </g>
      )}

      <line x1={plotL} x2={plotR} y1={plotB} y2={plotB} stroke={BORDER} strokeWidth="1.2" />
      <line x1={plotL} x2={plotL} y1={plotT} y2={plotB} stroke={BORDER} strokeWidth="1" opacity="0.5" />
    </svg>
  );
}

Histogram.displayName = "Histogram";
