/**
 * FX revaluation table — foreign-currency balances translated at booked vs. current rates,
 * with the unrealized gain/loss per row (`Money.fxRevalue`, half-even) and a footer totalling
 * base value and net unrealized G/L. Local balances format at their ISO minor units.
 */
export interface FxRevaluationRow {
  /** ISO currency code of the local balance ("EUR", "JPY", …). */
  currency: string;
  /** Account / desk label. */
  account?: string;
  /** Balance in local currency. */
  local: number | string;
  /** Rate the balance is carried at (base units per 1 local unit). */
  bookedRate: number | string;
  /** Current market rate. */
  currentRate: number | string;
}
export interface FxRevaluationTableProps {
  rows: FxRevaluationRow[];
  /** Base/reporting currency. @default "USD" */
  base?: string;
  /** Rate display decimals. @default 4 */
  rateDecimals?: number;
  /** Accessible caption / region label. */
  caption?: string;
  /** Row inspect handler `(row, index)`. */
  onRowClick?: (row: FxRevaluationRow, index: number) => void;
  /** Highlight one row. */
  selectedIndex?: number;
}
export declare function FxRevaluationTable(props: FxRevaluationTableProps): JSX.Element;
