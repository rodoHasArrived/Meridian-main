/**
 * Commitment bar — capital-commitment funding state for private capital vehicles.
 * Mirrors `CapitalCommitmentDto` / `PrivateAssetSummaryDto`: commitment, called,
 * distributed, current NAV. Renders a hairline funding bar (called share filled with
 * the accent) over a mono figure row — Committed · Called (with %) · Unfunded ·
 * Distributed (DPI) · NAV (TVPI). DPI = distributed / called and
 * TVPI = (distributed + nav) / called are derived only when their inputs are present;
 * omitted fields drop their figure instead of showing zero.
 *
 * @example
 * <CommitmentBar
 *   label="Blue Harbor Growth Fund III" vintage={2021} currency="USD"
 *   commitment={5000000} called={3100000} distributed={1240000} nav={2900000}
 * />
 */
export interface CommitmentBarProps extends React.HTMLAttributes<HTMLElement> {
  /** Total commitment amount (the bar's full width). */
  commitment: number;
  /** Capital called to date. @default 0 */
  called?: number;
  /** Distributions to date — enables the Distributed figure + DPI. */
  distributed?: number;
  /** Current NAV — enables the NAV figure + TVPI. */
  nav?: number;
  /** ISO currency code for formatting. @default "USD" */
  currency?: string;
  /** Vehicle name shown above the bar. */
  label?: React.ReactNode;
  /** Vintage year, shown right-aligned in the header. */
  vintage?: number | string;
}
export declare function CommitmentBar(props: CommitmentBarProps): JSX.Element;
