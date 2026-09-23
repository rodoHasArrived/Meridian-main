// Meridian evidence link (Concrete) — a reference to an evidence artifact (the "evidence"
// concept is central: readiness, reconciliation, provider, and report-pack evidence).
// Mirrors the EvidenceStatus model (Unknown | Ready | ReviewRequired | Blocked | Stale |
// Missing). Renders as a clickable chip: status dot, label, mono route, open arrow.
// Renders as an <a> when `href` is set, otherwise a <button>.
import type { AnchorHTMLAttributes, ButtonHTMLAttributes, ReactNode } from "react";
import { injectStyle } from "./inject-style";
import { normalizeSeverity } from "./status";

const CSS = `
.mds-evidence{display:inline-flex;align-items:center;gap:8px;max-width:100%;
  border:1px solid var(--border,#E4E3DE);border-radius:var(--radius-chip,2px);
  background:var(--bg-medium,#EDEAE4);padding:5px 9px;font-family:var(--font-body);font-size:12px;
  color:var(--text-primary,#22252A);text-decoration:none;cursor:pointer;
  transition:border-color 120ms ease,background-color 120ms ease;}
.mds-evidence:hover{border-color:var(--border-hover,#C6C3BB);background:var(--bg-hover,#F0EEE9);}
.mds-evidence:focus-visible{outline:var(--focus-ring,2px solid var(--ws-accent,#A85436));outline-offset:var(--focus-ring-offset,2px);}
.mds-evidence__dot{width:7px;height:7px;border-radius:50%;flex:0 0 auto;background:var(--state-muted-fg,#5E666F);}
.mds-evidence--ready .mds-evidence__dot{background:var(--state-healthy-fg,#2C5C40);}
.mds-evidence--review .mds-evidence__dot{background:var(--state-paper-fg,#8C4429);}
.mds-evidence--action .mds-evidence__dot{background:var(--state-warn-fg,#68450E);}
.mds-evidence--blocked .mds-evidence__dot{background:var(--state-danger-fg,#7E332D);}
.mds-evidence__label{font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-evidence__route{font-family:var(--font-data,monospace);font-size:10px;color:var(--text-muted,#5E666F);
  overflow:hidden;text-overflow:ellipsis;white-space:nowrap;min-width:0;}
.mds-evidence__arrow{margin-left:auto;color:var(--text-muted,#5E666F);flex:0 0 auto;font-size:11px;}
`;

type EvidenceLinkOwnProps = {
  /** Evidence label. */
  label?: ReactNode;
  /** Evidence status (Ready, ReviewRequired, Blocked, Stale, Missing, …). @default "info" */
  status?: string;
  /** Mono route / identifier (e.g. an evidence:// URI). */
  route?: ReactNode;
  /** If set, renders an anchor to this href. */
  href?: string;
  /** Click handler (when used as a button). */
  onOpen?: () => void;
};

export type EvidenceLinkProps = EvidenceLinkOwnProps &
  Omit<
    AnchorHTMLAttributes<HTMLAnchorElement> & ButtonHTMLAttributes<HTMLButtonElement>,
    keyof EvidenceLinkOwnProps | "onClick"
  >;

/**
 * Evidence link — a clickable reference to an evidence artifact.
 *
 * @example
 * <EvidenceLink label="Recon pack" status="Ready" route="evidence://recon/2026-06" href="#" />
 * <EvidenceLink label="Approval"   status="Missing" route="evidence://approval/pending" onOpen={open} />
 */
export function EvidenceLink({
  label,
  status = "info",
  route,
  href,
  onOpen,
  className,
  ...rest
}: EvidenceLinkProps) {
  injectStyle("evidence-link", CSS);
  const sev = normalizeSeverity(status);
  const cls = `mds-evidence mds-evidence--${sev}${className ? " " + className : ""}`;
  const safeRest = { ...rest };
  delete (safeRest as { onClick?: unknown }).onClick;
  const body = (
    <>
      <span className="mds-evidence__dot" aria-hidden="true" />
      {label && <span className="mds-evidence__label">{label}</span>}
      {route && <span className="mds-evidence__route">{route}</span>}
      <span className="mds-evidence__arrow" aria-hidden="true">
        ↗
      </span>
    </>
  );

  if (href) {
    return (
      <a className={cls} href={href} onClick={onOpen} {...safeRest}>
        {body}
      </a>
    );
  }
  return (
    <button type="button" className={cls} onClick={onOpen} {...safeRest}>
      {body}
    </button>
  );
}
