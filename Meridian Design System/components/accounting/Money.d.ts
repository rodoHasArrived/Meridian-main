/**
 * `Money` — carrier object exposing the shared money.js helpers on the namespace (they are
 * lowercase functions, reached the same way as `TableHooks`). These are the exact functions the
 * accounting components use internally, so consumer arithmetic always matches rendered figures.
 */
export interface MoneyHelpers {
  /** Format a money value — { currency, decimals=2, parens, zeroDash, signed }. */
  formatMoney(value: number | string, opts?: {
    currency?: string; decimals?: number; parens?: boolean; zeroDash?: boolean; signed?: boolean;
  }): string;
  /** Compact metric money — $12.4M. Never inside ledger columns. */
  formatMoneyCompact(value: number | string, opts?: { currency?: string; decimals?: number; signed?: boolean }): string;
  /** Share/contract quantity — thousands separators, up to maxDecimals (default 4). */
  formatQuantity(value: number | string, maxDecimals?: number): string;
  /** Decimal fraction → basis points: formatBps(0.00124) → "+12.4 bp". */
  formatBps(fraction: number | string, opts?: { signed?: boolean; decimals?: number }): string;
  /** Round under an explicit policy. "half-even" (books default) | "half-up" | "trunc". */
  roundMoney(value: number | string, decimals?: number, mode?: "half-even" | "half-up" | "trunc"): number;
  /**
   * Split a total across weights so parts sum EXACTLY to the rounded total (largest-remainder,
   * at the currency minor unit). allocateAmount(100, [1,1,1]) → [33.34, 33.33, 33.33].
   */
  allocateAmount(total: number | string, weights: Array<number | string>, decimals?: number): number[];
  /** FX-translate at `rate` (base units per 1 local unit), half-even rounded. */
  convertAmount(value: number | string, rate: number | string, decimals?: number): number;
  /** Sum money-ish values, ignoring blanks/NaN. */
  sumAmounts(values: Array<number | string>): number;
  /** Coerce "(1,234.00)" / "$5,000" / numbers → Number (NaN if unparseable). */
  toNumber(value: unknown): number;
  /** Currency code → leading symbol; literal symbols pass through. */
  currencySymbol(code?: string): string;
  /** ISO-4217 minor units — JPY → 0, BHD → 3, default 2. */
  currencyDecimals(code?: string): number;
  /** Normal balance side for an account type — asset/expense → "debit"; liability/equity/revenue → "credit". */
  accountNormalSide(type?: string): "debit" | "credit";
  /** Whole days from `from` to `to` (ISO strings or Dates) — positive when `to` is later. */
  daysBetween(from: string | Date, to: string | Date): number;
  /**
   * Aging bucket index for days past due against ascending boundaries.
   * Default [1, 31, 61, 91] → 0 Current · 1 (1–30) · 2 (31–60) · 3 (61–90) · 4 (90+).
   */
  agingBucketIndex(daysPastDue: number | string, boundaries?: number[]): number;
  /**
   * Collapse raw journal postings ({ code?, account, type?, debit?, credit? }) into one
   * trial-balance row per account, the net placed on the side of its sign. Feed to TrialBalance.
   */
  buildTrialBalance(
    postings: Array<{ code?: string; account: string; type?: string; debit?: number | string; credit?: number | string }>,
    opts?: { decimals?: number },
  ): Array<{ code?: string; account: string; type?: string; debit: number; credit: number }>;
  /** Straight-line schedule over `periods` — remainder-exact via allocateAmount, sums to total. */
  amortizeStraightLine(total: number | string, periods: number, decimals?: number): number[];
  /** Revalue a local balance from booked rate to current rate → { booked, current, gainLoss } in base. */
  fxRevalue(localAmount: number | string, bookedRate: number | string, currentRate: number | string, decimals?: number):
    { booked: number; current: number; gainLoss: number };
}
export declare const Money: MoneyHelpers;
