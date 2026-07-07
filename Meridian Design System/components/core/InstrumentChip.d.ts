/**
 * InstrumentChip — the identity micro-chip for a tradable instrument: mono symbol, small-caps
 * venue, and an asset-class block letter on a washed semantic tint (E equity · F ETF ·
 * U future · O option · X fx · C crypto · B bond). Use it wherever a symbol appears outside a
 * table cell — watchlists, order tickets, inspectors. With `onClick` it renders as a button.
 *
 * @example
 * <InstrumentChip symbol="AAPL" venue="XNAS" assetClass="eq" />
 * <InstrumentChip symbol="ESU6" venue="CME" assetClass="fut" size="sm" onClick={select} />
 */
export interface InstrumentChipProps extends React.HTMLAttributes<HTMLElement> {
  symbol: string;
  /** Exchange / venue code, rendered small-caps muted (e.g. "XNAS", "CME"). */
  venue?: string;
  /** "eq" | "etf" | "fut" | "opt" | "fx" | "crypto" | "bond" (long or short forms). @default "eq" */
  assetClass?: string;
  /** @default "md" */
  size?: "sm" | "md";
  /** Accent border + wash — the chip is the current selection. @default false */
  selected?: boolean;
  /** Renders the chip as a real button. */
  onClick?: () => void;
}
export declare function InstrumentChip(props: InstrumentChipProps): JSX.Element;
