import * as React from "react";
/**
 * PnLCalendar — month-grid daily P&L view. Monday-first, UTC dates. By default cells wash
 * green/red at alpha-10 (alpha-20 past half the month's max magnitude); heat={false} drops
 * the wash for the ink-on-paper sheet style, where the signed mono value alone carries the
 * color. Optional per-day intraday micro-bars draw around a dashed midline. The footer
 * double-rules the month total, statement-style.
 *
 * @example
 * <PnLCalendar month="2026-06" values={{ "2026-06-01": 1240.5, "2026-06-02": -3180 }} />
 * <PnLCalendar month="2026-06" values={dailyPnl} bars={intradayPnl} heat={false} cellHeight={56} />
 */
export interface PnLCalendarProps {
  /** Month as "YYYY-MM". */
  month: string;
  /** Map of "YYYY-MM-DD" → signed P&L. Only valid dates in the displayed month
   * contribute to its total and heat scale. Missing days render empty (non-trading). */
  values?: Record<string, number>;
  /** Map of "YYYY-MM-DD" → intraday signed values; renders micro-bars around a dashed midline. */
  bars?: Record<string, number[]> | null;
  /** Green/red alpha wash on cells. Set false for the sheet style (values + bars only). @default true */
  heat?: boolean;
  /** Override the signed formatter (default: signed, thousands-separated, 0dp). */
  valueFormat?: (v: number) => string;
  /** Min cell height, px. @default 48 */
  cellHeight?: number;
  /** Render the month-total footer. @default true */
  showTotal?: boolean;
  style?: React.CSSProperties;
}
export declare function PnLCalendar(props: PnLCalendarProps): React.JSX.Element;
