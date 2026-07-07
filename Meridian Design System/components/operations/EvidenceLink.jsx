// Meridian evidence link — a reference to an evidence artifact (the "evidence"
// concept is central: readiness, reconciliation, provider, and report-pack evidence).
// Mirrors the EvidenceStatus model (Unknown | Ready | ReviewRequired | Blocked |
// Stale | Missing). Renders as a clickable chip: status dot, label, mono route, arrow.
import React from "react";
import { normalizeSeverity } from "./status";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-evidence{display:inline-flex;align-items:center;gap:8px;max-width:100%;
  border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-chip,2px);
  background:var(--bg-medium,#F5F7FA);padding:5px 9px;font-family:var(--font-body);font-size:12px;
  color:var(--text-primary,#22272E);text-decoration:none;cursor:pointer;
  transition:border-color 120ms ease,background-color 120ms ease;}
.mds-evidence:hover{border-color:var(--border-hover,#B8C2CC);background:var(--bg-hover,#F1F4F7);}
.mds-evidence:focus-visible{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-evidence__dot{width:7px;height:7px;border-radius:50%;flex:0 0 auto;background:var(--state-muted-fg,#6E7781);}
.mds-evidence--ready .mds-evidence__dot{background:var(--state-healthy-fg,#16885F);}
.mds-evidence--review .mds-evidence__dot{background:var(--state-paper-fg,#2F6F8F);}
.mds-evidence--action .mds-evidence__dot{background:var(--state-warn-fg,#8A520E);}
.mds-evidence--blocked .mds-evidence__dot{background:var(--state-danger-fg,#BA3F55);}
.mds-evidence__label{font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-evidence__route{font-family:var(--font-data,monospace);font-size:10px;color:var(--text-muted,#59636F);
  overflow:hidden;text-overflow:ellipsis;white-space:nowrap;min-width:0;}
.mds-evidence__arrow{margin-left:auto;color:var(--text-muted,#59636F);flex:0 0 auto;font-size:11px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "evidence-link");
  el.textContent = css;
  document.head.appendChild(el);
}

export function EvidenceLink({ label, status = "info", route, href, onOpen, className = "", ...rest }) {
  inject();
  const sev = normalizeSeverity(status);
  const Tag = href ? "a" : "button";
  const extra = href ? { href } : { type: "button" };
  return (
    <Tag
      className={`mds-evidence mds-evidence--${sev}${className ? " " + className : ""}`}
      onClick={onOpen}
      {...extra}
      {...rest}
    >
      <span className="mds-evidence__dot" aria-hidden="true" />
      {label && <span className="mds-evidence__label">{label}</span>}
      {route && <span className="mds-evidence__route">{route}</span>}
      <span className="mds-evidence__arrow" aria-hidden="true">{"\u2197"}</span>
    </Tag>
  );
}
