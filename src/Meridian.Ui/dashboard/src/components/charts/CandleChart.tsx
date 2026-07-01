// Meridian candlestick chart — price/time axes with labels, gridlines, MA overlays, a crosshair
// readout with a right-axis price chip, and an optional volume histogram subpane. Up candles use
// --chart-equity (green, hollow body); down use --chart-drawdown (red, filled). SVG, flex-fills
// its container — give the wrapper a definite height (see ChartCard).
import { niceTicks } from "./ticks";

export interface Candle {
  /** X-axis time label, e.g. "10:48". */
  t: string;
  o: number;
  h: number;
  l: number;
  c: number;
  /** Volume (drives the histogram subpane). */
  v?: number;
}

export interface CandleOverlay {
  label: string;
  /** CSS color (token var), e.g. "var(--accent, #2F6F8F)". */
  color: string;
  /** Moving-average window in bars. */
  win: number;
}

export interface CandleChartProps {
  bars: Candle[];
  /** MA overlays. @default MA20 (accent) + MA50 (warning) */
  overlays?: CandleOverlay[];
  /** Bar index to mark with the crosshair + price chip. */
  crosshairIndex?: number | null;
  /** Show the volume histogram subpane. @default true */
  showVolume?: boolean;
  /** Approx. number of price gridlines. @default 6 */
  priceTicks?: number;
  /** Approx. number of time labels. @default 7 */
  timeTicks?: number;
}

const DEFAULT_OVERLAYS: CandleOverlay[] = [
  { label: "MA20", color: "var(--accent, #2F6F8F)", win: 20 },
  { label: "MA50", color: "var(--chart-warning, #8A520E)", win: 50 }
];

const UP = "var(--chart-equity, #16885F)";
const DOWN = "var(--chart-drawdown, #BA3F55)";
const PLOT = "var(--chart-plot, #FFFFFF)";
const GRID = "var(--chart-grid, #CBD3DC)";
const AXIS = "var(--chart-axis, #59636F)";
const BORDER = "var(--chart-border, #99A5B2)";
const CROSSHAIR = "var(--chart-crosshair, #2F6F8F)";

export function CandleChart({
  bars,
  overlays = DEFAULT_OVERLAYS,
  crosshairIndex = null,
  showVolume = true,
  priceTicks = 6,
  timeTicks = 7
}: CandleChartProps) {
  const W = 960;
  const H = 540;
  const padL = 48;
  const padT = 14;
  const axisR = 64;
  const axisB = 28;
  const volH = showVolume ? 80 : 0;
  const volGap = showVolume ? 12 : 0;
  const plotL = padL;
  const plotR = W - axisR;
  const priceT = padT;
  const priceB = H - axisB - volH - volGap;
  const volB = H - axisB;
  const volT = priceB + volGap;

  const denom = bars.length > 1 ? bars.length - 1 : 1;
  const his = bars.map((b) => b.h);
  const los = bars.map((b) => b.l);
  let max = his.length ? Math.max(...his) : 1;
  let min = los.length ? Math.min(...los) : 0;
  const padv = (max - min) * 0.08 || 1;
  max += padv;
  min -= padv;
  const maxVol = Math.max(...bars.map((b) => b.v || 0), 0) || 1;

  const x = (i: number) => plotL + i * ((plotR - plotL) / denom);
  const yP = (v: number) => priceT + (max - v) * ((priceB - priceT) / (max - min || 1));
  const yV = (v: number) => volB - (v / maxVol) * (volB - volT);
  const bw = Math.max(2.2, ((plotR - plotL) / (bars.length || 1)) * 0.68);

  const ma = (win: number): Array<number | null> =>
    bars.map((_, i) => {
      if (i < win - 1) return null;
      const slice = bars.slice(i - win + 1, i + 1);
      return slice.reduce((acc, b) => acc + b.c, 0) / slice.length;
    });
  const maPath = (win: number) => {
    let d = "";
    let started = false;
    ma(win).forEach((v, i) => {
      if (v == null) return;
      d += `${started ? "L" : "M"}${x(i).toFixed(1)},${yP(v).toFixed(1)} `;
      started = true;
    });
    return d.trim();
  };

  const pTicks = niceTicks(min, max, priceTicks);
  const tStep = Math.max(1, Math.round((bars.length || 1) / timeTicks));
  const ch = crosshairIndex != null && bars[crosshairIndex] ? bars[crosshairIndex] : null;

  const axFont = { fontFamily: "var(--font-data, monospace)", fontSize: 11 };

  return (
    <svg
      viewBox={`0 0 ${W} ${H}`}
      preserveAspectRatio="none"
      style={{ width: "100%", height: "100%", display: "block" }}
      role="img"
      aria-label="Candlestick price chart"
    >
      <rect x="0" y="0" width={W} height={H} fill={PLOT} />
      {/* price gridlines + right-axis labels */}
      {pTicks.map((v, k) => (
        <g key={k}>
          <line x1={plotL} x2={plotR} y1={yP(v)} y2={yP(v)} stroke={GRID} strokeWidth="1" />
          <text x={plotR + 7} y={yP(v) + 4} fill={AXIS} style={axFont}>
            {v.toFixed(2)}
          </text>
        </g>
      ))}
      {/* time gridlines + bottom labels */}
      {bars.map((b, i) =>
        i % tStep === 0 || i === bars.length - 1 ? (
          <g key={"t" + i}>
            <line x1={x(i)} x2={x(i)} y1={priceT} y2={priceB} stroke={GRID} strokeWidth="1" opacity="0.6" />
            <text x={x(i)} y={H - 7} fill={AXIS} textAnchor="middle" style={axFont}>
              {b.t}
            </text>
          </g>
        ) : null
      )}
      {/* axis frame */}
      <line x1={plotL} x2={plotR} y1={priceB} y2={priceB} stroke={BORDER} strokeWidth="1" />
      <line x1={plotR} x2={plotR} y1={priceT} y2={volB} stroke={BORDER} strokeWidth="1" />

      {/* volume histogram */}
      {showVolume &&
        bars.map((b, i) => {
          const up = b.c >= b.o;
          const top = yV(b.v || 0);
          return (
            <rect
              key={"v" + i}
              x={x(i) - bw / 2}
              y={top}
              width={bw}
              height={Math.max(0, volB - top)}
              fill={up ? UP : DOWN}
              opacity="0.28"
            />
          );
        })}
      {showVolume && <line x1={plotL - 1} x2={plotR} y1={volB} y2={volB} stroke={BORDER} strokeWidth="1.2" />}

      {/* candles */}
      {bars.map((b, i) => {
        const up = b.c >= b.o;
        const col = up ? UP : DOWN;
        const bodyY = yP(Math.max(b.o, b.c));
        const bodyH = Math.max(1, Math.abs(yP(b.o) - yP(b.c)));
        return (
          <g key={i}>
            <line x1={x(i)} x2={x(i)} y1={yP(b.h)} y2={yP(b.l)} stroke={col} strokeWidth="1" />
            <rect x={x(i) - bw / 2} y={bodyY} width={bw} height={bodyH} fill={up ? PLOT : col} stroke={col} strokeWidth="1" />
          </g>
        );
      })}

      {/* MA overlays */}
      {overlays.map((o, k) => (
        <path key={k} d={maPath(o.win)} fill="none" stroke={o.color} strokeWidth="1.5" />
      ))}

      {/* crosshair */}
      {ch && crosshairIndex != null && (
        <g>
          <line
            x1={x(crosshairIndex)}
            x2={x(crosshairIndex)}
            y1={priceT}
            y2={volB}
            stroke={CROSSHAIR}
            strokeWidth="1.2"
            strokeDasharray="4 2"
            opacity="0.8"
          />
          <line x1={plotL} x2={plotR} y1={yP(ch.c)} y2={yP(ch.c)} stroke={CROSSHAIR} strokeWidth="1.2" strokeDasharray="4 2" opacity="0.8" />
          <circle cx={x(crosshairIndex)} cy={yP(ch.c)} r="4" fill={CROSSHAIR} opacity="0.9" />
          <rect x={plotR + 2} y={yP(ch.c) - 10} width={axisR - 4} height="20" fill={CROSSHAIR} rx="2" opacity="0.95" />
          <text
            x={plotR + axisR / 2}
            y={yP(ch.c) + 5}
            fill="#fff"
            textAnchor="middle"
            style={{ ...axFont, fontWeight: 600, fontSize: 13 }}
          >
            {ch.c.toFixed(2)}
          </text>
        </g>
      )}
    </svg>
  );
}

CandleChart.displayName = "CandleChart";
