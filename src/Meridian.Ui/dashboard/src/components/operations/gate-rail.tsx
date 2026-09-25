// Meridian gate rail (Concrete) — the operations pipeline as a connected horizontal
// stepper. Renders the operator workflow: BrokerIngest → SecurityMaster → LedgerPosting
// → Reconciliation → Approval (or any gate list). Each node is tinted by its status
// (Passed / InProgress / ReviewRequired / Blocked / NotStarted) and the connector into a
// node turns green once the preceding gate has Passed.
import type { CSSProperties, ReactNode } from "react";
import { injectStyle } from "./inject-style";
import { normalizeSeverity, humanizeStatus, type SeverityKey } from "./status";

const CSS = `
.mds-gates{display:flex;list-style:none;margin:0;padding:0;gap:0;min-width:0;}
.mds-gate{position:relative;flex:1 1 0;display:flex;flex-direction:column;align-items:center;
  gap:6px;text-align:center;padding:0 4px;min-width:0;}
.mds-gate:not(:first-child)::before{content:"";position:absolute;top:13px;left:calc(-50% + 14px);
  width:calc(100% - 28px);height:2px;background:var(--mds-gate-line,var(--border,#E4E3DE));z-index:0;}
.mds-gate__node{position:relative;z-index:1;display:inline-flex;align-items:center;justify-content:center;
  width:28px;height:28px;border-radius:50%;border:1.5px solid var(--severity-info-bd,#E4E3DE);
  background:var(--bg-panel,var(--bg-light,#FBFAF8));color:var(--severity-info-fg,#5E666F);
  font-family:var(--font-data,monospace);font-size:11px;font-weight:700;}
.mds-gate--ready .mds-gate__node{border-color:var(--severity-ready-fg,#2C5C40);background:var(--severity-ready-bg,rgba(58,122,86,.10));color:var(--severity-ready-fg,#2C5C40);}
.mds-gate--review .mds-gate__node{border-color:var(--severity-review-fg,#8C4429);background:var(--severity-review-bg,rgba(168,84,54,.10));color:var(--severity-review-fg,#8C4429);}
.mds-gate--action .mds-gate__node{border-color:var(--severity-action-fg,#68450E);background:var(--severity-action-bg,rgba(138,92,18,.11));color:var(--severity-action-fg,#68450E);}
.mds-gate--blocked .mds-gate__node{border-color:var(--severity-blocked-fg,#7E332D);background:var(--severity-blocked-bg,rgba(168,68,60,.10));color:var(--severity-blocked-fg,#7E332D);}
.mds-gate__label{font-family:var(--font-body);font-size:11px;font-weight:600;color:var(--text-primary,#22252A);line-height:1.2;}
.mds-gate__status{font-family:var(--font-data,monospace);font-size:9px;font-weight:700;letter-spacing:.04em;
  text-transform:uppercase;color:var(--text-muted,#5E666F);}
`;

const GLYPH: Partial<Record<SeverityKey, string>> = { ready: "✓", blocked: "!" };

export interface GateRailGate {
  /** Stable key (e.g. an OperationsGateKey). */
  key?: string;
  /** Gate label shown under the node. */
  label: ReactNode;
  /** Gate status string — normalized to ready · review · action · blocked · info. */
  status: string;
  /** Override the auto-humanized status caption. */
  statusLabel?: ReactNode;
}

export interface GateRailProps {
  gates: GateRailGate[];
  className?: string;
}

/**
 * Gate rail — the operations pipeline as a connected horizontal stepper.
 *
 * @example
 * <GateRail gates={[
 *   { key: "BrokerIngest",   label: "Broker ingest",   status: "Passed" },
 *   { key: "SecurityMaster", label: "Security master", status: "Passed" },
 *   { key: "LedgerPosting",  label: "Ledger posting",  status: "InProgress" },
 *   { key: "Reconciliation", label: "Reconciliation",  status: "ReviewRequired" },
 *   { key: "Approval",       label: "Approval",        status: "NotStarted" },
 * ]} />
 */
export function GateRail({ gates = [], className }: GateRailProps) {
  injectStyle("gate-rail", CSS);
  return (
    <ol className={`mds-gates${className ? " " + className : ""}`}>
      {gates.map((g, i) => {
        const sev = normalizeSeverity(g.status);
        const prevReady = i > 0 && normalizeSeverity(gates[i - 1].status) === "ready";
        const line = prevReady ? "var(--severity-ready-fg,#2C5C40)" : "var(--border,#E4E3DE)";
        const style = { "--mds-gate-line": line } as CSSProperties;
        return (
          <li
            key={g.key || i}
            className={`mds-gate mds-gate--${sev}`}
            style={style}
            aria-label={`${g.label}: ${humanizeStatus(g.status)}`}
          >
            <span className="mds-gate__node">{GLYPH[sev] ?? i + 1}</span>
            <span className="mds-gate__label">{g.label}</span>
            <span className="mds-gate__status">{g.statusLabel || humanizeStatus(g.status)}</span>
          </li>
        );
      })}
    </ol>
  );
}
