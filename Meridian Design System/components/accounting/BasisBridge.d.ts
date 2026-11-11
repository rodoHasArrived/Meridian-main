/**
 * Basis bridge — one economic event under every accounting basis it touches
 * (statutory, GAAP, tax, management, local). Investment accounting is
 * basis-specific by nature: a classification override may apply to statutory
 * only while GAAP and tax keep the configured treatment. The bridge makes that
 * scope explicit before anything is applied.
 *
 * Cells that differ from the configured treatment are marked **structurally**
 * (rail, marker, and label), never by color alone, and an unpopulated cell
 * prints `—` rather than being left blank.
 *
 * @example
 * <BasisBridge
 *   bases={[
 *     { id:"stat", label:"Statutory", meta:"Selected" },
 *     { id:"gaap", label:"GAAP", reference: true },
 *     { id:"tax",  label:"Tax",  reference: true },
 *   ]}
 *   rows={[
 *     { label:"Security classification", values:{ stat:"Corporate bond", gaap:"Corporate bond", tax:"Corporate bond" } },
 *     { label:"Ledger account", changed:["stat"],
 *       values:{ stat:"Private placement debt", gaap:"Corporate bonds", tax:"Tax investments" } },
 *     { label:"Yield method", changed:["stat"], values:{ stat:"Overridden", gaap:"Configured", tax:"Configured" } },
 *   ]}
 *   footer={[{ label:"Approval",
 *     values:{ stat:"Required", gaap:"Not required", tax:"Not required" },
 *     tones:{ stat:"exception" } }]} />
 */
export interface BasisColumn {
  id: string;
  label: string;
  /** Second header line — "Selected", "Configured", version. */
  meta?: string;
  /** Marks a basis as unchanged reference (muted values). */
  reference?: boolean;
}
export interface BasisRow {
  id?: string;
  /** Treatment attribute — classification, ledger account, yield method, schedule. */
  label: string;
  /** Basis id → displayed value. */
  values: Record<string, React.ReactNode>;
  /** Basis ids whose value differs from the configured treatment. */
  changed?: string[];
}
export interface BasisFooterRow {
  label: string;
  values: Record<string, React.ReactNode>;
  /** Basis id → data-condition key used to tint the footer cell. */
  tones?: Record<string, string>;
}
export interface BasisBridgeProps extends React.HTMLAttributes<HTMLDivElement> {
  bases: BasisColumn[];
  rows: BasisRow[];
  /** First-column heading. */
  attributeLabel?: string;
  /** Single emphasized footer row — approval, effective date, or posting state. */
  footer?: BasisFooterRow[];
}
export declare function BasisBridge(props: BasisBridgeProps): JSX.Element;
