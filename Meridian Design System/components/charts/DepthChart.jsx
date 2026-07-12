// Meridian DepthChart — order-book market depth (volume profile). Cumulative bid size rises
// as a green step area toward the mid from the left; cumulative ask size rises red to the
// right. The mid price marks the spread. Reads the order book at a glance for an operator.
import React from "react";

export function DepthChart({
  bids = [],                    // [{price, size}] — any order
  asks = [],                    // [{price, size}]
  mid = null,                   // defaults to midpoint of best bid / best ask
  priceFmt = (p) => p.toFixed(2),
  sizeFmt = (s) => s >= 1000 ? `${(s / 1000).toFixed(1)}k` : String(s),
  sizeTicks = 4,
  priceTicks = 7,
}) {
  const W = 960, H = 360;
  const padT = 16, axisB = 26, axisL = 8, axisR = 52;
  const plotL = axisL, plotR = W - axisR, plotT = padT, plotB = H - axisB;

  // sort outward from the mid
  const bidsS = [...bids].sort((a, b) => b.price - a.price); // best (highest) first
  const asksS = [...asks].sort((a, b) => a.price - b.price); // best (lowest) first
  const bestBid = bidsS.length ? bidsS[0].price : null;
  const bestAsk = asksS.length ? asksS[0].price : null;
  const midPrice = mid != null ? mid : (bestBid != null && bestAsk != null ? (bestBid + bestAsk) / 2 : (bestBid ?? bestAsk ?? 0));

  // cumulative depth outward from mid
  let cum = 0;
  const bidPts = bidsS.map((b) => { cum += b.size; return { price: b.price, depth: cum }; });
  cum = 0;
  const askPts = asksS.map((a) => { cum += a.size; return { price: a.price, depth: cum }; });

  const allP = [...bids, ...asks].map((d) => d.price);
  const pLo = allP.length ? Math.min(...allP) : midPrice - 1;
  const pHi = allP.length ? Math.max(...allP) : midPrice + 1;
  const pSpan = (pHi - pLo) || 1;
  const maxDepth = Math.max(1, ...bidPts.map((p) => p.depth), ...askPts.map((p) => p.depth));

  const x = (p) => plotL + ((p - pLo) / pSpan) * (plotR - plotL);
  const y = (d) => plotB - (d / maxDepth) * (plotB - plotT);

  // build step paths starting at mid (depth 0) outward
  const bidStep = () => {
    if (!bidPts.length) return "";
    let d = `M${x(midPrice).toFixed(1)},${y(0).toFixed(1)}`;
    let prevDepth = 0;
    for (const p of bidPts) {
      d += ` L${x(p.price).toFixed(1)},${y(prevDepth).toFixed(1)} L${x(p.price).toFixed(1)},${y(p.depth).toFixed(1)}`;
      prevDepth = p.depth;
    }
    const last = bidPts[bidPts.length - 1];
    d += ` L${x(last.price).toFixed(1)},${y(0).toFixed(1)} Z`;
    return d;
  };
  const askStep = () => {
    if (!askPts.length) return "";
    let d = `M${x(midPrice).toFixed(1)},${y(0).toFixed(1)}`;
    let prevDepth = 0;
    for (const p of askPts) {
      d += ` L${x(p.price).toFixed(1)},${y(prevDepth).toFixed(1)} L${x(p.price).toFixed(1)},${y(p.depth).toFixed(1)}`;
      prevDepth = p.depth;
    }
    const last = askPts[askPts.length - 1];
    d += ` L${x(last.price).toFixed(1)},${y(0).toFixed(1)} Z`;
    return d;
  };

  const labelFont = { fontFamily: "var(--font-data)", fontSize: 11 };
  const sTicks = Array.from({ length: sizeTicks + 1 }, (_, i) => Math.round((i / sizeTicks) * maxDepth));
  const pStep = Math.max(1, Math.round((priceTicks)));
  const priceLabels = Array.from({ length: pStep + 1 }, (_, i) => pLo + (i / pStep) * pSpan);

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" style={{ width: "100%", height: "100%", display: "block" }}>
      <rect x="0" y="0" width={W} height={H} fill="var(--chart-plot)" />
      {[...new Set(sTicks)].map((s, k) => (
        <g key={k}>
          <line x1={plotL} x2={plotR} y1={y(s)} y2={y(s)} stroke="var(--chart-grid)" strokeWidth="0.8" opacity="0.55" />
          <text x={plotR + 8} y={y(s) + 4} fill="var(--chart-axis)" textAnchor="start" style={labelFont}>{sizeFmt(s)}</text>
        </g>
      ))}

      <path d={bidStep()} fill="var(--chart-equity)" opacity="0.18" />
      <path d={bidStep()} fill="none" stroke="var(--chart-equity)" strokeWidth="1.6" />
      <path d={askStep()} fill="var(--chart-drawdown)" opacity="0.18" />
      <path d={askStep()} fill="none" stroke="var(--chart-drawdown)" strokeWidth="1.6" />

      {/* mid / spread */}
      <line x1={x(midPrice)} x2={x(midPrice)} y1={plotT} y2={plotB} stroke="var(--chart-crosshair)" strokeWidth="1.2" strokeDasharray="4 3" />
      <rect x={x(midPrice) - 38} y={plotT - 2} width="76" height="17" fill="var(--chart-crosshair)" opacity="0.95" />
      <text x={x(midPrice)} y={plotT + 11} fill="#fff" textAnchor="middle" style={{ ...labelFont, fontWeight: 600 }}>mid {priceFmt(midPrice)}</text>

      {priceLabels.map((p, i) => (
        <text key={"p" + i} x={x(p)} y={H - 8} fill="var(--chart-axis)" textAnchor="middle" style={labelFont}>{priceFmt(p)}</text>
      ))}
      <line x1={plotL} x2={plotR} y1={plotB} y2={plotB} stroke="var(--chart-border)" strokeWidth="1.2" />
    </svg>
  );
}

DepthChart.displayName = "DepthChart";
