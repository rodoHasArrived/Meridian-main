/**
 * Chart card — mirrors `ChartCardStyle`: a header row (title + optional small-caps OHLC/stat
 * readout + actions) over a framed plot area that hosts a CandleChart or EquityCurve.
 */
export interface ChartReadoutItem {
  label: string;
  value: React.ReactNode;
  /** Override value color (e.g. green/red). */
  color?: string;
}
export interface ChartCardProps {
  title: string;
  subtitle?: string;
  /** Inline OHLC / stat readout shown beside the title. */
  readout?: ChartReadoutItem[];
  /** Right-aligned controls (timeframe buttons, etc.). */
  actions?: React.ReactNode;
  /** Plot-area height in px. @default 320 */
  height?: number;
  children?: React.ReactNode;
  style?: React.CSSProperties;
}
export declare function ChartCard(props: ChartCardProps): JSX.Element;
