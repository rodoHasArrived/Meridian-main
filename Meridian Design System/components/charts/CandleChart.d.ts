/**
 * Candlestick price chart — mirrors the desktop ChartingPage candlestick pane. Renders
 * price/time axes with labels, gridlines, MA overlays, an optional volume histogram subpane,
 * and a crosshair readout with a right-axis price chip. Up candles use --chart-equity (green,
 * hollow body), down use --chart-drawdown (red, filled).
 */
export interface Candle {
  /** X-axis time label, e.g. "10:48". */
  t: string;
  o: number; h: number; l: number; c: number;
  /** Volume (drives the histogram subpane). */
  v?: number;
}
export interface CandleOverlay {
  label: string;
  /** CSS color (token var), e.g. "var(--accent)". */
  color: string;
  /** Moving-average window in bars. */
  win: number;
}
export interface CandleChartProps {
  bars: Candle[];
  /** MA overlays. @default MA20 (accent) + MA50 (orange) */
  overlays?: CandleOverlay[];
  /** Bar index to mark with the crosshair + price chip. */
  crosshairIndex?: number | null;
  /** Show the volume histogram subpane. @default true */
  showVolume?: boolean;
  /** Approx. number of price gridlines. @default 5 */
  priceTicks?: number;
  /** Approx. number of time labels. @default 6 */
  timeTicks?: number;
}
export declare function CandleChart(props: CandleChartProps): JSX.Element;
