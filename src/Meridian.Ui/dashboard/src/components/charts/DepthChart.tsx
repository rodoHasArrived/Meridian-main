// Meridian DepthChart — order-book market depth (volume profile). Cumulative bid size rises as a
// green step area toward the mid from the left; cumulative ask size rises red to the right. The
// mid price marks the spread. Reads a live order book at a glance. Explicit-height svg.

export interface DepthLevel {
  price: number;
  size: number;
}

export interface DepthChartProps {
  /** Bid levels — any order; cumulated outward from the mid. */
  bids: DepthLevel[];
  /** Ask levels — any order; cumulated outward from the mid. */
  asks: DepthLevel[];
  /** Mid price. @default midpoint of best bid / best ask */
  mid?: number | null;
  /** Format for price-axis values. @default p => p.toFixed(2) */
  priceFmt?: (p: number) => string;
  /** Format for cumulative-size axis. @default 1.2k-style */
  sizeFmt?: (s: number) => string;
  /** @default 4 */
  sizeTicks?: number;
  /** @default 7 */
  priceTicks?: number;
}

const PLOT = "var(--chart-plot, #FFFFFF)";
const GRID = "var(--chart-grid, #CBD3DC)";
const AXIS = "var(--chart-axis, #59636F)";
const BORDER = "var(--chart-border, #99A5B2)";
const CROSSHAIR = "var(--chart-crosshair, #2F6F8F)";
const UP = "var(--chart-equity, #16885F)";
const DOWN = "var(--chart-drawdown, #BA3F55)";

export function DepthChart({
  bids = [],
  asks = [],
  mid = null,
  priceFmt = (p) => p.toFixed(2),
  sizeFmt = (s) => (s >= 1000 ? `${(s / 1000).toFixed(1)}k` : String(s)),
  sizeTicks = 4,
  priceTicks = 7
}: DepthChartProps) {
  const W = 960;
  const H = 360;
  const padT = 16;
  const axisB = 26;
  const axisL = 8;
  const axisR = 52;
  const plotL = axisL;
  const plotR = W - axisR;
  const plotT = padT;
  const plotB = H - axisB;

  // sort outward from the mid
  const bidsS = [...bids].sort((a, b) => b.price - a.price); // best (highest) first
  const asksS = [...asks].sort((a, b) => a.price - b.price); // best (lowest) first
  const bestBid = bidsS.length ? bidsS[0].price : null;
  const bestAsk = asksS.length ? asksS[0].price : null;
  const midPrice =
    mid != null ? mid : bestBid != null && bestAsk != null ? (bestBid + bestAsk) / 2 : bestBid ?? bestAsk ?? 0;

  // cumulative depth outward from mid
  let cum = 0;
  const bidPts = bidsS.map((b) => {
    cum += b.size;
    return { price: b.price, depth: cum };
  });
  cum = 0;
  const askPts = asksS.map((a) => {
    cum += a.size;
    return { price: a.price, depth: cum };
  });

  const allP = [...bids, ...asks].map((d) => d.price);
  const pLo = allP.length ? Math.min(...allP) : midPrice - 1;
  const pHi = allP.length ? Math.max(...allP) : midPrice + 1;
  const pSpan = pHi - pLo || 1;
  const maxDepth = Math.max(1, ...bidPts.map((p) => p.depth), ...askPts.map((p) => p.depth));

  const x = (p: number) => plotL + ((p - pLo) / pSpan) * (plotR - plotL);
  const y = (d: number) => plotB - (d / maxDepth) * (plotB - plotT);

  const step = (pts: Array<{ price: number; depth: number }>) => {
    if (!pts.length) return "";
    let d = `M${x(midPrice).toFixed(1)},${y(0).toFixed(1)}`;
    let prevDepth = 0;
    for (const p of pts) {
      d += ` L${x(p.price).toFixed(1)},${y(prevDepth).toFixed(1)} L${x(p.price).toFixed(1)},${y(p.depth).toFixed(1)}`;
      prevDepth = p.depth;
    }
    const last = pts[pts.length - 1];
    d += ` L${x(last.price).toFixed(1)},${y(0).toFixed(1)} Z`;
    return d;
  };

  const bidPath = step(bidPts);
  const askPath = step(askPts);

  const labelFont = { fontFamily: "var(--font-data, monospace)", fontSize: 11 };
  const sTicks = Array.from({ length: sizeTicks + 1 }, (_, i) => Math.round((i / sizeTicks) * maxDepth));
  const pStep = Math.max(1, Math.round(priceTicks));
  const priceLabels = Array.from({ length: pStep + 1 }, (_, i) => pLo + (i / pStep) * pSpan);

  return (
    <svg
      viewBox={`0 0 ${W} ${H}`}
      preserveAspectRatio="none"
      style={{ width: "100%", height: "100%", display: "block" }}
      role="img"
      aria-label="Order-book depth chart"
    >
      <rect x="0" y="0" width={W} height={H} fill={PLOT} />
      {[...new Set(sTicks)].map((s, k) => (
        <g key={k}>
          <line x1={plotL} x2={plotR} y1={y(s)} y2={y(s)} stroke={GRID} strokeWidth="0.8" opacity="0.55" />
          <text x={plotR + 8} y={y(s) + 4} fill={AXIS} textAnchor="start" style={labelFont}>
            {sizeFmt(s)}
          </text>
        </g>
      ))}

      {bidPath && <path d={bidPath} fill={UP} opacity="0.18" />}
      {bidPath && <path d={bidPath} fill="none" stroke={UP} strokeWidth="1.6" />}
      {askPath && <path d={askPath} fill={DOWN} opacity="0.18" />}
      {askPath && <path d={askPath} fill="none" stroke={DOWN} strokeWidth="1.6" />}

      {/* mid / spread */}
      <line x1={x(midPrice)} x2={x(midPrice)} y1={plotT} y2={plotB} stroke={CROSSHAIR} strokeWidth="1.2" strokeDasharray="4 3" />
      <rect x={x(midPrice) - 38} y={plotT - 2} width="76" height="17" fill={CROSSHAIR} opacity="0.95" />
      <text x={x(midPrice)} y={plotT + 11} fill="#fff" textAnchor="middle" style={{ ...labelFont, fontWeight: 600 }}>
        mid {priceFmt(midPrice)}
      </text>

      {priceLabels.map((p, i) => (
        <text key={"p" + i} x={x(p)} y={H - 8} fill={AXIS} textAnchor="middle" style={labelFont}>
          {priceFmt(p)}
        </text>
      ))}
      <line x1={plotL} x2={plotR} y1={plotB} y2={plotB} stroke={BORDER} strokeWidth="1.2" />
    </svg>
  );
}

DepthChart.displayName = "DepthChart";
