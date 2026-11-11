/**
 * Value envelope — the single contract for what is known about a figure:
 * **condition · authority · evidence · clocks · materiality**. One envelope
 * replaces the stack of `DataCondition` + `ProvenanceChip` + `ValueAuthority` +
 * `FreshnessIndicator` + `MaterialityBadge` beside a number, and makes it
 * structurally impossible to render a figure without its provenance.
 *
 * Three disclosure tiers, always the same three:
 * 1. `ValueEnvelope` — the value with derived inline marks.
 * 2. its hover envelope — condition, authority, clocks, evidence count.
 * 3. `EnvelopePanel` — the full envelope in an inspector rail or drawer.
 *
 * `envelopeCheck` runs the coherence rules on the data (not on the screen):
 * settlement before trade, calculated before its evidence, posting outside its
 * reporting period, evidence after the close deadline, override with no authority.
 * `EnvelopeCoherence` rolls that up for every envelope on a surface.
 *
 * Law: `guidelines/VALUE_ENVELOPE.md`. Time roles: `guidelines/TIME_SEMANTICS.md`.
 *
 * @example
 * <ValueEnvelope value="4.35%" condition="overridden" authority="desk" align="right"
 *   detail="desk mark" evidence={[{ label: "Desk note", ref: "MK-2211" }]}
 *   clocks={{ asOf: "2026-08-11 20:00:00Z", evidenceAt: "2026-08-11 19:41:00Z" }} />
 */
import type { MeridianDataCondition } from "./DataCondition";

export type MeridianAuthority =
  | "source" | "derived" | "desk" | "override" | "estimate" | "unattributed";

export interface EnvelopeClocks {
  trade?: string; settlement?: string; effective?: string; received?: string;
  posting?: string; valuation?: string; closeDeadline?: string; asOf?: string;
  evidenceAt?: string; calculatedAt?: string;
  /** Inclusive window — a posting outside it fails coherence. */
  reportingPeriod?: { start: string; end: string };
  fiscalPeriod?: { start: string; end: string };
}
export interface EnvelopeEvidence { id?: string; label: string; ref?: string; href?: string }
export interface EnvelopeMateriality { material: boolean; threshold?: string }

export interface MeridianValueEnvelope {
  /** Pre-formatted value text; `null` renders `—` (or `Missing`). */
  value?: string | number | null;
  label?: string;
  condition?: MeridianDataCondition | string;
  /** Qualifier folded into the notation — age for stale, reason for overridden. */
  detail?: string;
  authority?: MeridianAuthority;
  evidence?: EnvelopeEvidence[];
  clocks?: EnvelopeClocks;
  materiality?: EnvelopeMateriality;
}

export interface ValueEnvelopeProps extends MeridianValueEnvelope, React.HTMLAttributes<HTMLSpanElement> {
  /** `"right"` for numeric table columns. */
  align?: "left" | "right";
  /** Open the tier-3 panel (inspector rail, drawer). */
  onInspect?: () => void;
}
export declare function ValueEnvelope(props: ValueEnvelopeProps): JSX.Element;

export interface EnvelopePanelProps extends React.HTMLAttributes<HTMLDivElement> {
  envelope: MeridianValueEnvelope;
  title?: string;
}
export declare function EnvelopePanel(props: EnvelopePanelProps): JSX.Element;

export interface EnvelopeMarkProps {
  kind?: "condition" | "authority" | "evidence" | "clock" | "material";
  glyph?: string;
  bad?: boolean;
  title?: string;
}
export declare function EnvelopeMark(props: EnvelopeMarkProps): JSX.Element;

export interface EnvelopeCoherenceProps extends React.HTMLAttributes<HTMLDivElement> {
  envelopes: MeridianValueEnvelope[];
}
export declare function EnvelopeCoherence(props: EnvelopeCoherenceProps): JSX.Element;

export interface EnvelopeViolation { rule: string; text: string }
/** Also reachable as `ValueEnvelope.check` — consumers read it off the carrier. */
export declare function envelopeCheck(envelope: MeridianValueEnvelope): EnvelopeViolation[];
export declare const ENVELOPE_MARKS: Record<string, { glyph: string; label: string }>;
export declare const AUTHORITIES: Record<MeridianAuthority, { label: string; note: string }>;
