/**
 * Provenance chip — the recurring evidence tuple that every family-office and
 * reconciliation read model carries (`SourceSystem`, `SourceDocumentId`, `AsOfDate`,
 * `ValuationDate`, `EvidenceCompleteness`, `ReconciliationStatus`, `LastReviewedBy/At`).
 * A compact inline annotation for table cells and fact rows: status dot (the WORST of
 * completeness vs. reconciliation) · mono source system · as-of date, with the full
 * tuple in the hover title. Renders a `<button>` when `onOpen` is set (to jump to the
 * evidence route), otherwise a `<span>`.
 *
 * @example
 * <ProvenanceChip sourceSystem="Northern Trust" asOfDate="2026-06-30"
 *   completeness="Complete" reconciliation="Matched" />
 * <ProvenanceChip sourceSystem="Manual upload" asOfDate="2026-05-31"
 *   completeness="Partial" reconciliation="BreaksDetected"
 *   sourceDocumentId="stmt-2026-05-nt.pdf" onOpen={openEvidence} />
 */
export interface ProvenanceChipProps extends React.HTMLAttributes<HTMLElement> {
  /** Originating system / custodian (rendered mono uppercase). */
  sourceSystem?: string;
  /** Backing document id — hover title only. */
  sourceDocumentId?: string;
  /** As-of date (rendered inline). */
  asOfDate?: string;
  /** Valuation date — hover title only. */
  valuationDate?: string;
  /** Evidence completeness status (Complete, Partial, Missing, Stale, …). */
  completeness?: string;
  /** Reconciliation status (Matched, BreaksDetected, Resolved, …). */
  reconciliation?: string;
  /** Last reviewer — hover title only. */
  reviewedBy?: string;
  /** Last review time (ISO UTC) — hover title only. */
  reviewedAtUtc?: string;
  /** Makes the chip a button (e.g. open the evidence drawer). */
  onOpen?: () => void;
}
export declare function ProvenanceChip(props: ProvenanceChipProps): JSX.Element;
