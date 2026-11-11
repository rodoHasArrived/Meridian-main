import type { ObjectRef } from "../core/ObjectModel";

/**
 * EvidenceRail — the persistent "why is this value what it is" plane of a workstation.
 *
 * Slot-based on purpose: a fixed `evidence={…}` schema would have to anticipate every domain's
 * proof shape and would be brittle within one release. The rail owns the reading ORDER, the
 * coverage denominator and the continuity of the evidence; the screen owns the bodies.
 *
 * @example
 * <EvidenceRail
 *   object={{ type: "position", id: "91282CKQ3", version: 18 }}
 *   coverage={{ attached: 99, required: 100, label: "sources" }}
 *   continuity="retained" freshness="15m" asOf="2026-07-31"
 *   counts={{ sources: 12, calculation: 3 }}
 *   sections={{
 *     summary: <EvidenceSummary />,
 *     sources: <EvidencePreview evidence={documents} />,
 *     calculation: <CalculationTrace trace={trace} />,
 *     lineage: <LineageGraph object={position} />,
 *   }}
 *   onSectionChange={setSection}
 * />
 */

export type EvidenceSectionId = "summary" | "sources" | "calculation" | "lineage" | "audit";

export interface EvidenceSectionMeta {
  id: EvidenceSectionId;
  label: string;
  /** The operator question the section answers, rendered above the body. */
  question: string;
}
/** The five sections, in reading order. Order is the rail's contract, not the screen's. */
export declare const EVIDENCE_SECTIONS: EvidenceSectionMeta[];

/** Degradation state of the evidence itself — pairs with `ContinuityFrame` on the work plane. */
export type EvidenceContinuity = "live" | "retained" | "partial" | "recovering" | "stale";

export interface EvidenceRailProps extends Omit<React.HTMLAttributes<HTMLElement>, "title"> {
  /** The object being explained. Canonical `ObjectRef` — never a local object union. */
  object?: ObjectRef;
  title?: React.ReactNode;
  /** Section bodies. A section omitted (or `null`) is not rendered — no blank tabs. */
  sections?: Partial<Record<EvidenceSectionId, React.ReactNode>>;
  /** Optional per-section counts shown on the tab (`{ sources: 12 }`). */
  counts?: Partial<Record<EvidenceSectionId, number>>;
  /** Controlled active section. Omit for internal state. */
  activeSection?: EvidenceSectionId;
  defaultSection?: EvidenceSectionId;
  onSectionChange?: (section: EvidenceSectionId) => void;
  /** Always exposes the denominator — `attached of required`, and what is outstanding. */
  coverage?: { attached?: number; required?: number; label?: string };
  continuity?: EvidenceContinuity;
  freshness?: React.ReactNode;
  asOf?: React.ReactNode;
  /** Hide the per-section operator question. @default false */
  showQuestion?: boolean;
  /** Tighter rail for split panes. @default false */
  dense?: boolean;
}
/** Tabs are a real tablist: arrow keys, Home/End, roving tabindex, one focusable tab. */
export declare function EvidenceRail(props: EvidenceRailProps): JSX.Element;
