/**
 * Evidence link — a reference to an evidence artifact (readiness, reconciliation,
 * provider, or report-pack evidence). Mirrors the `EvidenceStatus` model
 * (`Unknown | Ready | ReviewRequired | Blocked | Stale | Missing`). A clickable chip
 * with a status dot, label, mono route, and an open arrow. Renders as an `<a>` when
 * `href` is set, otherwise a `<button>`.
 *
 * @example
 * <EvidenceLink label="Recon pack" status="Ready" route="evidence://recon/2026-06" href="#" />
 * <EvidenceLink label="Approval"   status="Missing" route="evidence://approval/pending" onOpen={open} />
 */
export interface EvidenceLinkProps extends React.HTMLAttributes<HTMLElement> {
  /** Evidence label. */
  label?: React.ReactNode;
  /** Evidence status (Ready, ReviewRequired, Blocked, Stale, Missing, …). @default "info" */
  status?: string;
  /** Mono route / identifier (e.g. an evidence:// URI). */
  route?: React.ReactNode;
  /** If set, renders an anchor to this href. */
  href?: string;
  /** Click handler (when used as a button). */
  onOpen?: () => void;
}
export declare function EvidenceLink(props: EvidenceLinkProps): JSX.Element;
