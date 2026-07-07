/**
 * PnLCalendar — month-grid daily P&L heat view. Monday-first, UTC dates. Cells wash green/red
 * at alpha-10 (alpha-20 past half the month's max magnitude); values are mono with explicit
 * signs; the footer double-rules the month total, statement-style.
 *
 * @example
 * <PnLCalendar month="2026-06" values={{ "2026-06-01": 1240.5, "2026-06-02": -3180, "2026-06-03": 0 }} />
 * <PnLCalendar month="2026-06" values={dailyPnl} valueFormat={(v) => (v>0?"+":"") + v.toFixed(1) + " bps"} />
 */
export interface PnLCalendarProps {
  /** Month as "YYYY-MM". */
  month: string;
  /** Map of "YYYY-MM-DD" → signed P&L. Missing days render empty (non-trading). */
  values?: Record<string, number>;
  /** Override the signed formatter (default: signed, thousands-separated, 0dp). */
  valueFormat?: (v: number) => string;
  /** Min cell height, px. @default 48 */
  cellHeight?: number;
  /** Render the month-total footer. @default true */
  showTotal?: boolean;
  style?: React.CSSProperties;
}
export declare function PnLCalendar(props: PnLCalendarProps): JSX.Element;
