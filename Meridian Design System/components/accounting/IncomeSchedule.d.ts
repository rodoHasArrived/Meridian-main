import * as React from "react";
/**
 * Income schedule — the period-by-period table an investment accountant reads
 * instead of a generic chart: coupon accrual, premium amortization / discount
 * accretion, investment income, and the ending book value each period rolls to,
 * with an explicit state per row (`posted · accrued · scheduled · draft ·
 * estimated · reversed · overridden`).
 *
 * Row selection is committed and should synchronize the workspace: the selected
 * month's journal preview, the book-value rollforward, and the evidence rail.
 *
 * Build the rows with `Money.effectiveYieldSchedule({ par, cost, couponRate,
 * effectiveYield, periods })` so the schedule, the rollforward, and any
 * consumer-side figure agree to the cent.
 *
 * @example
 * const rows = Money.effectiveYieldSchedule({
 *   par: 10_000_000, cost: 9_875_000, couponRate: 0.0525, effectiveYield: 0.05418,
 *   periods: 3, labels: ["June 2026", "July 2026", "August 2026"],
 * }).map((r, i) => ({ ...r, state: i === 0 ? "accrued" : "scheduled" }));
 *
 * <IncomeSchedule rows={rows} selectedId={period} onSelect={setPeriod} />
 */
export interface IncomeScheduleRow {
  id?: string;
  /** Period label — "June 2026", "2026-06". */
  period: string;
  coupon: number;
  /** Discount accretion (+) or premium amortization (−). */
  accretion: number;
  income: number;
  /** Book value the period rolls to (alias: `endingBook`). */
  closingBook?: number;
  endingBook?: number;
  state?: "posted" | "accrued" | "scheduled" | "draft" | "estimated" | "reversed" | "overridden";
  /** Override the state text while keeping its marker and tone. */
  stateLabel?: string;
}
export interface IncomeScheduleProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect"> {
  rows: IncomeScheduleRow[];
  /** ISO code shown in the totals row; amounts are already in this currency. */
  currency?: string;
  selectedId?: string;
  onSelect?: (id: string) => void;
  showTotals?: boolean;
  maxHeight?: number;
  /** Column headings — rename for premium lots ("Premium amortization") or equities. */
  couponLabel?: string;
  accretionLabel?: string;
  incomeLabel?: string;
  bookLabel?: string;
}
export declare function IncomeSchedule(props: IncomeScheduleProps): React.JSX.Element;
