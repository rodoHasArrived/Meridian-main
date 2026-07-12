/**
 * ChartTooltip — floating readout for the chart interaction layer. Renders label/value rows
 * in a small card that follows the crosshair index (auto-flips near the right edge). Pairs
 * with `useChartCrosshair` / `useSyncedCursor` from `useChartCrosshair.js`. Place it inside
 * the crosshair-bound wrapper (which is `position:relative` via the hook's `bind`).
 *
 * @example
 * const cx = useSyncedCursor(bars.length);
 * <div {...cx.bind}>
 *   <CandleChart bars={bars} crosshairIndex={cx.index} />
 *   <ChartTooltip index={cx.index} count={bars.length}
 *     title={bars[cx.index]?.t}
 *     rows={cx.index != null ? [
 *       { label: "O", value: bars[cx.index].o.toFixed(2) },
 *       { label: "C", value: bars[cx.index].c.toFixed(2), color: "var(--green-dim)" },
 *     ] : []} />
 * </div>
 */
export interface ChartTooltipRow {
  label: React.ReactNode;
  value: React.ReactNode;
  /** Optional value color (e.g. green/red for up/down). */
  color?: string;
}
export interface ChartTooltipProps {
  /** Current crosshair index, or null when the cursor is outside the plot. */
  index: number | null;
  /** Total data points, for horizontal positioning. */
  count: number;
  title?: React.ReactNode;
  rows?: ChartTooltipRow[];
  /** Plot inset fractions — match the chart. @default 0.05 / 0.93 */
  plotLeft?: number;
  plotRight?: number;
}
export declare function ChartTooltip(props: ChartTooltipProps): JSX.Element | null;
