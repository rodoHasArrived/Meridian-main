export type PeriodState = "future" | "open" | "soft-closed" | "hard-closed" | "locked" | "adjustment" | "reopened" | "prior-period" | "archive";
export interface AccountingPeriod {
  value: string;
  label: string;
  state?: PeriodState;
  fiscalYear?: string;
  /** Human reporting period, e.g. "July 2026". */
  reportingPeriod?: string;
  /** Machine posting period, e.g. "2026-07". */
  postingPeriod?: string;
  asOf?: string;
  /** Exact lock time — "August 5, 2026 at 18:00 UTC". */
  lockedAfter?: string;
  /** e.g. "Controller approval required". */
  lateEntryPolicy?: string;
  /** The current user's rights in this period, e.g. "Read-only". */
  permission?: string;
  adjustmentPeriod?: string;
  /** Why editing or posting is unavailable. Defaults to a sentence derived from `state`. */
  explanation?: string;
  /** Overrides the derived explanation entirely. */
  description?: string;
}
export interface AccountingPeriodSelectorProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onChange"> {
  periods: AccountingPeriod[];
  value?: string;
  onChange?: (value: string, period?: AccountingPeriod) => void;
  label?: string;
  disabled?: boolean;
  showDetail?: boolean;
  /** The distinct date roles in effect — never collapse these into one field. */
  dates?: Partial<Record<"transaction" | "trade" | "settlement" | "accounting" | "posting" | "reporting" | "asOf", React.ReactNode>>;
}
export declare function AccountingPeriodSelector(props: AccountingPeriodSelectorProps): JSX.Element;
