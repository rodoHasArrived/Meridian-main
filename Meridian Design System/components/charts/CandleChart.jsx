// Meridian candlestick chart — price/time axes, gridlines, MA overlays, crosshair readout,
// optional volume subpane. Up = chart-equity (green), down = chart-drawdown (red).
// Renders through ChartFrame: real-pixel geometry, shared ticks, one code path.
import React from "react";
import { Fmt, domainOf } from "./viz.js";
import { ChartFrame, AxisChip } from "./ChartFrame.jsx";

export function CandleChart({ ariaLabel, ariaDescription, accessibleData,
  bars,                        // [{ t, o, h, l, c, v? }]
  overlays = [{ label: "MA20", color: "var(--brand-signal,#C56343)", win: 20 }, { label: "MA50", color: "var(--orange)", win: 50 }],
  crosshairIndex = null,
  showVolume = true,
  priceFmt = Fmt.price,
  priceTicks = 6,
  timeTicks = 7,
  height = 420,
  rail = 64,
}) {
  const domain = domainOf([bars.map((b) => b.h), bars.map((b) => b.l)], 0.08);
  const maxVol = Math.max(...bars.map((b) => b.v || 0), 0) || 1;
  const ma = (win) => bars.map((_, i) => {
    if (i < win - 1) return null;
    const s = bars.slice(i - win + 1, i + 1);
    return s.reduce((a, b) => a + b.c, 0) / s.length;
  });
  const ch = crosshairIndex != null && bars[crosshairIndex] ? bars[crosshairIndex] : null;
  const panes = [
    { key: "price", flex: 3.4, domain, ticks: priceTicks, fmt: priceFmt },
    ...(showVolume ? [{ key: "vol", flex: 1, domain: [0, maxVol], ticks: 0 }] : []),
  ];
  return (
    <ChartFrame ariaLabel={ariaLabel} ariaDescription={ariaDescription} accessibleData={accessibleData} height={height} n={bars.length} xLabels={bars.map((b) => b.t)} xTicks={timeTicks}
      rail={rail} panes={panes} crosshairIndex={crosshairIndex}>
      {(g) => {
        const P = g.pane("price"), V = showVolume ? g.pane("vol") : null;
        const bw = Math.max(2.2, ((g.plotR - g.plotL) / (bars.length || 1)) * 0.68);
        const maPath = (win) => {
          let d = "", started = false;
          ma(win).forEach((v, i) => { if (v == null) return; d += `${started ? "L" : "M"}${g.x(i).toFixed(1)},${P.y(v).toFixed(1)} `; started = true; });
          return d.trim();
        };
        return (
          <g>
            {V && bars.map((b, i) => {
              const up = b.c >= b.o;
              return <rect key={"v" + i} x={g.x(i) - bw / 2} y={V.y(b.v || 0)} width={bw} height={Math.max(0, V.b - V.y(b.v || 0))}
                fill={up ? "var(--chart-equity)" : "var(--chart-drawdown)"} opacity="0.28" />;
            })}
            {bars.map((b, i) => {
              const up = b.c >= b.o; const col = up ? "var(--chart-equity)" : "var(--chart-drawdown)";
              const bodyY = P.y(Math.max(b.o, b.c)), bodyH = Math.max(1, Math.abs(P.y(b.o) - P.y(b.c)));
              return (
                <g key={i}>
                  <line x1={g.x(i)} x2={g.x(i)} y1={P.y(b.h)} y2={P.y(b.l)} stroke={col} strokeWidth="1" />
                  <rect x={g.x(i) - bw / 2} y={bodyY} width={bw} height={bodyH} fill={up ? "var(--chart-plot)" : col} stroke={col} strokeWidth="1" />
                </g>
              );
            })}
            {overlays.map((o, k) => <path key={k} d={maPath(o.win)} fill="none" stroke={o.color} strokeWidth="1.5" />)}
            {ch && (
              <g>
                <line x1={g.plotL} x2={g.plotR} y1={P.y(ch.c)} y2={P.y(ch.c)} stroke="var(--chart-crosshair)" strokeWidth="1.2" strokeDasharray="4 2" opacity="0.8" />
                <circle cx={g.x(crosshairIndex)} cy={P.y(ch.c)} r="4" fill="var(--chart-crosshair)" opacity="0.9" />
                <AxisChip x={g.plotR + 2} y={P.y(ch.c)} width={rail - 4} text={priceFmt(ch.c)} />
              </g>
            )}
          </g>
        );
      }}
    </ChartFrame>
  );
}

CandleChart.displayName = "CandleChart";
