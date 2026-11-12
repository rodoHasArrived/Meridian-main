// Meridian DepthChart — order-book market depth (volume profile). Cumulative bid size rises
// as a green step area toward the mid from the left; cumulative ask size rises red to the
// right. The mid price marks the spread. Renders through ChartFrame (real-pixel; the price
// axis stays value-scaled inside the marks).
import React from "react";
import { niceTicks, Fmt } from "./viz.js";
import { ChartFrame, AXIS_FONT } from "./ChartFrame.jsx";

export function DepthChart({ ariaLabel, ariaDescription, accessibleData,
  bids = [],                    // [{price, size}] — any order
  asks = [],                    // [{price, size}]
  mid = null,                   // defaults to midpoint of best bid / best ask
  priceFmt = Fmt.price,
  sizeFmt = Fmt.volume,
  sizeTicks = 4,
  priceTicks = 7,
  height = 320,
}) {
  const bidsS = [...bids].sort((a, b) => b.price - a.price);
  const asksS = [...asks].sort((a, b) => a.price - b.price);
  const bestBid = bidsS.length ? bidsS[0].price : null;
  const bestAsk = asksS.length ? asksS[0].price : null;
  const midPrice = mid != null ? mid : (bestBid != null && bestAsk != null ? (bestBid + bestAsk) / 2 : (bestBid ?? bestAsk ?? 0));
  let cum = 0;
  const bidPts = bidsS.map((b) => { cum += b.size; return { price: b.price, depth: cum }; });
  cum = 0;
  const askPts = asksS.map((a) => { cum += a.size; return { price: a.price, depth: cum }; });
  const allP = [...bids, ...asks].map((d) => d.price);
  const pLo = allP.length ? Math.min(...allP) : midPrice - 1;
  const pHi = allP.length ? Math.max(...allP) : midPrice + 1;
  const pSpan = (pHi - pLo) || 1;
  const maxDepth = Math.max(1, ...bidPts.map((p) => p.depth), ...askPts.map((p) => p.depth));
  return (
    <ChartFrame ariaLabel={ariaLabel} ariaDescription={ariaDescription} accessibleData={accessibleData} height={height} n={Math.max(1, bids.length + asks.length)} rail={52} padL={8} padT={16}
      panes={[{ key: "d", domain: [0, maxDepth], ticks: sizeTicks, fmt: sizeFmt }]}>
      {(g) => {
        const D = g.pane("d");
        const x = (p) => g.plotL + ((p - pLo) / pSpan) * (g.plotR - g.plotL);
        const step = (pts) => {
          if (!pts.length) return "";
          let d = `M${x(midPrice).toFixed(1)},${D.y(0).toFixed(1)}`;
          let prev = 0;
          for (const p of pts) {
            d += ` L${x(p.price).toFixed(1)},${D.y(prev).toFixed(1)} L${x(p.price).toFixed(1)},${D.y(p.depth).toFixed(1)}`;
            prev = p.depth;
          }
          const last = pts[pts.length - 1];
          d += ` L${x(last.price).toFixed(1)},${D.y(0).toFixed(1)} Z`;
          return d;
        };
        return (
          <g>
            <path d={step(bidPts)} fill="var(--chart-equity)" opacity="0.18" />
            <path d={step(bidPts)} fill="none" stroke="var(--chart-equity)" strokeWidth="1.6" />
            <path d={step(askPts)} fill="var(--chart-drawdown)" opacity="0.18" />
            <path d={step(askPts)} fill="none" stroke="var(--chart-drawdown)" strokeWidth="1.6" />
            <line x1={x(midPrice)} x2={x(midPrice)} y1={D.t} y2={D.b} stroke="var(--chart-crosshair)" strokeWidth="1.2" strokeDasharray="4 3" />
            <rect x={x(midPrice) - 38} y={D.t - 2} width="76" height="17" fill="var(--chart-crosshair)" opacity="0.95" />
            <text x={x(midPrice)} y={D.t + 11} fill="var(--text-on-fill,#fff)" textAnchor="middle" style={{ ...AXIS_FONT, fontWeight: 600 }}>mid {priceFmt(midPrice)}</text>
            {niceTicks(pLo, pHi, priceTicks).filter((p) => p >= pLo && p <= pHi).map((p, i) => (
              <text key={"p" + i} x={x(p)} y={height - 8} fill="var(--chart-axis)" textAnchor="middle" style={AXIS_FONT}>{priceFmt(p)}</text>
            ))}
          </g>
        );
      }}
    </ChartFrame>
  );
}

DepthChart.displayName = "DepthChart";
