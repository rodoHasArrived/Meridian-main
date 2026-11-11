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
  width:calc(100% - 28px);height:2px;background:var(--mds-gate-line,var(--border,#D2CDC3));z-index:0;}
.mds-gate__node{position:relative;z-index:1;display:inline-flex;align-items:center;justify-content:center;
  width:28px;height:28px;border-radius:50%;border:1.5px solid var(--severity-info-bd,#D2CDC3);
  background:var(--bg-panel,var(--bg-light,#F8F7F3));color:var(--severity-info-fg,#606870);
  font-family:var(--font-data,monospace);font-size:11px;font-weight:700;}
.mds-gate--ready .mds-gate__node{border-color:var(--severity-ready-fg,#225840);background:var(--severity-ready-bg,rgba(46,118,85,.10));color:var(--severity-ready-fg,#225840);}
.mds-gate--review .mds-gate__node{border-color:var(--severity-review-fg,#944A32);background:var(--severity-review-bg,rgba(197,99,67,.10));color:var(--severity-review-fg,#944A32);}
.mds-gate--action .mds-gate__node{border-color:var(--severity-action-fg,#724D11);background:var(--severity-action-bg,rgba(152,103,23,.11));color:var(--severity-action-fg,#724D11);}
.mds-gate--blocked .mds-gate__node{border-color:var(--severity-blocked-fg,#883832);background:var(--severity-blocked-bg,rgba(181,75,66,.10));color:var(--severity-blocked-fg,#883832);}
.mds-gate__label{font-family:var(--font-body);font-size:11px;font-weight:600;color:var(--text-primary,#20252B);line-height:1.2;}
.mds-gate__status{font-family:var(--font-data,monospace);font-size:9px;font-weight:700;letter-spacing:.04em;
  text-transform:uppercase;color:var(--text-muted,#606870);}
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
        const line = prevReady ? "var(--severity-ready-fg,#225840)" : "var(--border,#D2CDC3)";
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
