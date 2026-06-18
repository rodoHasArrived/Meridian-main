// Meridian candlestick chart — mirrors ChartingPage's LiveCharts candlestick pane:
// price/time axes with labels, gridlines, MA overlays, crosshair readout, optional
// volume histogram subpane. Up = chart-equity (green), down = chart-drawdown (red).
import React from "react";

function niceTicks(min, max, count) {
  const span = max - min || 1;
  const raw = span / count;
  const mag = Math.pow(10, Math.floor(Math.log10(raw)));
  const norm = raw / mag;
  const step = (norm >= 5 ? 5 : norm >= 2 ? 2 : 1) * mag;
  const start = Math.ceil(min / step) * step;
  const out = [];
  for (let v = start; v <= max + 1e-9; v += step) out.push(+v.toFixed(6));
  return out;
}

export function CandleChart({
  bars, overlays = [{ label: "MA20", color: "var(--accent)", win: 20 }, { label: "MA50", color: "var(--orange)", win: 50 }],
  crosshairIndex = null, showVolume = true, priceTicks = 6, timeTicks = 7,
}) {
  const W = 960, H = 540;
  const padL = 48, padT = 14, axisR = 64, axisB = 28;
  const volH = showVolume ? 80 : 0, volGap = showVolume ? 12 : 0;
  const plotL = padL, plotR = W - axisR;
  const priceT = padT, priceB = H - axisB - volH - volGap;
  const volT = priceB + volGap, volB = H - axisB;

  const his = bars.map(b => b.h), los = bars.map(b => b.l);
  let max = Math.max(...his), min = Math.min(...los);
  const padv = (max - min) * 0.08; max += padv; min -= padv;
  const maxVol = Math.max(...bars.map(b => b.v || 0)) || 1;

  const x = (i) => plotL + i * ((plotR - plotL) / (bars.length - 1));
  const yP = (v) => priceT + (max - v) * ((priceB - priceT) / (max - min));
  const yV = (v) => volB - (v / maxVol) * (volB - volT);
  const bw = Math.max(2.2, (plotR - plotL) / bars.length * 0.68);

  const ma = (win) => bars.map((_, i) => {
    if (i < win - 1) return null;
    const s = bars.slice(i - win + 1, i + 1);
    return s.reduce((a, b) => a + b.c, 0) / s.length;
  });
  const maPath = (win) => {
    let d = "", started = false;
    ma(win).forEach((v, i) => { if (v == null) return; d += `${started ? "L" : "M"}${x(i).toFixed(1)},${yP(v).toFixed(1)} `; started = true; });
    return d.trim();
  };

  const pTicks = niceTicks(min, max, priceTicks);
  const tStep = Math.max(1, Math.round(bars.length / timeTicks));
  const ch = crosshairIndex != null && bars[crosshairIndex] ? bars[crosshairIndex] : null;

  const axFont = { fontFamily: "var(--font-data)", fontSize: 11 };
  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" style={{ width: "100%", height: "100%", display: "block" }}>
      <rect x="0" y="0" width={W} height={H} fill="var(--chart-plot)" />
      {/* price gridlines + right-axis labels */}
      {pTicks.map((v, k) => (
        <g key={k}>
          <line x1={plotL} x2={plotR} y1={yP(v)} y2={yP(v)} stroke="var(--chart-grid)" strokeWidth="1" />
          <text x={plotR + 7} y={yP(v) + 4} fill="var(--chart-axis)" style={axFont}>{v.toFixed(2)}</text>
        </g>
      ))}
      {/* time gridlines + bottom labels */}
      {bars.map((b, i) => (i % tStep === 0 || i === bars.length - 1) ? (
        <g key={"t" + i}>
          <line x1={x(i)} x2={x(i)} y1={priceT} y2={priceB} stroke="var(--chart-grid)" strokeWidth="1" opacity="0.6" />
          <text x={x(i)} y={H - 7} fill="var(--chart-axis)" textAnchor="middle" style={axFont}>{b.t}</text>
        </g>
      ) : null)}
      {/* axis frame */}
      <line x1={plotL} x2={plotR} y1={priceB} y2={priceB} stroke="var(--chart-border)" strokeWidth="1" />
      <line x1={plotR} x2={plotR} y1={priceT} y2={volB} stroke="var(--chart-border)" strokeWidth="1" />

      {/* volume histogram */}
      {showVolume && bars.map((b, i) => {
        const up = b.c >= b.o;
        return <rect key={"v" + i} x={x(i) - bw / 2} y={yV(b.v || 0)} width={bw} height={Math.max(0, volB - yV(b.v || 0))}
          fill={up ? "var(--chart-equity)" : "var(--chart-drawdown)"} opacity="0.28" />;
      })}
      {showVolume && <line x1={plotL - 1} x2={plotR} y1={volB} y2={volB} stroke="var(--chart-border)" strokeWidth="1.2" />}

      {/* candles */}
      {bars.map((b, i) => {
        const up = b.c >= b.o; const col = up ? "var(--chart-equity)" : "var(--chart-drawdown)";
        const bodyY = yP(Math.max(b.o, b.c)), bodyH = Math.max(1, Math.abs(yP(b.o) - yP(b.c)));
        return (
          <g key={i}>
            <line x1={x(i)} x2={x(i)} y1={yP(b.h)} y2={yP(b.l)} stroke={col} strokeWidth="1" />
            <rect x={x(i) - bw / 2} y={bodyY} width={bw} height={bodyH} fill={up ? "var(--chart-plot)" : col} stroke={col} strokeWidth="1" />
          </g>
        );
      })}

      {/* MA overlays */}
      {overlays.map((o, k) => <path key={k} d={maPath(o.win)} fill="none" stroke={o.color} strokeWidth="1.5" />)}

      {/* crosshair */}
      {ch && (
        <g>
          <line x1={x(crosshairIndex)} x2={x(crosshairIndex)} y1={priceT} y2={volB} stroke="var(--chart-crosshair)" strokeWidth="1.2" strokeDasharray="4 2" opacity="0.8" />
          <line x1={plotL} x2={plotR} y1={yP(ch.c)} y2={yP(ch.c)} stroke="var(--chart-crosshair)" strokeWidth="1.2" strokeDasharray="4 2" opacity="0.8" />
          <circle cx={x(crosshairIndex)} cy={yP(ch.c)} r="4" fill="var(--chart-crosshair)" opacity="0.9" />
          <g>
            <rect x={plotR + 2} y={yP(ch.c) - 10} width={axisR - 4} height="20" fill="var(--chart-crosshair)" rx="4" opacity="0.95" />
            <text x={plotR + axisR / 2} y={yP(ch.c) + 5} fill="#fff" textAnchor="middle" style={{ ...axFont, fontWeight: 600, fontSize: 13 }}>{ch.c.toFixed(2)}</text>
          </g>
        </g>
      )}
    </svg>
  );
}
