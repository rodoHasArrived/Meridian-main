import type { MeridianValueEnvelope } from "./ValueEnvelope";

/**
 * Evidence inspector — the synchronized contextual rail of an analytical
 * workspace. It carries the selected value, its source and freshness, the
 * data-state contract, the calculation/transformation path, and the next
 * action. It never restates the primary view.
 *
 * Three exports compose it:
 * - `EvidenceInspector` — shell + selected-value block (rail + tinted surface).
 * - `EvidenceSection` — a titled block: `fields` rows, or arbitrary children
 *   (a `DataConditionLegend`, `IntervalBar` stack, links…).
 * - `ProvenanceTrace` — the source → transform → model → output chain, one step
 *   per node, optionally selectable to drive a lineage graph.
 *
 * Desktop width is `--inspector-width` (352px). On mobile the same content
 * becomes an inline disclosure or bottom sheet *after* the primary evidence.
 *
 * @example
 * <EvidenceInspector value="4.45%" caption="Current · 10Y · +11 bp vs 1M" condition="confirmed">
 *   <EvidenceSection title="Evidence" fields={[
 *     { label: "Source", value: "Refinitiv" },
 *     { label: "Observed", value: "11:29:42 UTC" },
 *     { label: "Quality", value: "Confirmed · 98%" },
 *   ]} />
 *   <EvidenceSection title="Data-state contract">
 *     <DataConditionLegend items={contract} />
 *   </EvidenceSection>
 *   <EvidenceSection title="Provenance trace">
 *     <ProvenanceTrace steps={trace} selectedId={node} onSelect={setNode} />
 *   </EvidenceSection>
 * </EvidenceInspector>
 */
export interface EvidenceInspectorProps extends React.HTMLAttributes<HTMLElement> {
  /** Small-caps label above the selection ("Selected observation"). */
  eyebrow?: string;
  /** The selected value, pre-formatted. Omit to render the empty message. */
  value?: React.ReactNode;
  /** One line of context under the value ("Current · 10Y · +11 bp vs 1M"). */
  caption?: React.ReactNode;
  /** Data condition of the selected value — adds marker + label under the caption. */
  condition?: string;
  /** Shown when nothing is selected — never leave the rail blank. */
  emptyMessage?: string;
  /** `EvidenceSection` children. */
  children?: React.ReactNode;
}
export declare function EvidenceInspector(props: EvidenceInspectorProps): JSX.Element;

export interface EvidenceField {
  label: string;
  value: React.ReactNode;
  /**
   * Full envelope facets for this figure — condition, authority, evidence, clocks, materiality.
   * The value renders through `ValueEnvelope`, so the condition arrives as a glyph with a title
   * and not a tint alone. Law: `guidelines/VALUE_ENVELOPE.md`.
   */
  envelope?: Omit<MeridianValueEnvelope, "value">;
  /** Legacy one-facet spelling — a condition key. Becomes the envelope's `condition`. */
  tone?: string;
}
export interface EvidenceSectionProps extends React.HTMLAttributes<HTMLElement> {
  title?: string;
  /** Right-aligned text action in the section header. */
  action?: string;
  onAction?: () => void;
  /** Label/value rows — the default body. */
  fields?: EvidenceField[];
  /** Caveat or method note rendered under the body in muted 11px. */
  note?: string;
  children?: React.ReactNode;
}
export declare function EvidenceSection(props: EvidenceSectionProps): JSX.Element;

export interface ProvenanceStep {
  id?: string;
  label: string;
  /** Secondary mono line — snapshot time, version, row count. */
  meta?: string;
  /** `done` (filled) · `current` (accented) · `pending` (hollow). */
  state?: "done" | "current" | "pending";
}
export interface ProvenanceTraceProps extends React.OlHTMLAttributes<HTMLOListElement> {
  steps: ProvenanceStep[];
  selectedId?: string;
  /** Makes each step a button — pair with a `LineageGraph` selection. */
  onSelect?: (id: string) => void;
}
export declare function ProvenanceTrace(props: ProvenanceTraceProps): JSX.Element;
