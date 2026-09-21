// Meridian provenance chip — the recurring evidence tuple on family-office and
// reconciliation read models (SourceSystem, SourceDocumentId, AsOfDate, ValuationDate,
// EvidenceCompleteness, ReconciliationStatus, LastReviewedBy/At). A compact inline
// annotation: completeness dot · mono source system · as-of date. Full tuple in the
// hover title. Renders a <button> when onOpen is set, otherwise a <span>.
import React from "react";
import { normalizeSeverity, humanizeStatus } from "./status";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-prov{display:inline-flex;align-items:center;gap:6px;width:fit-content;max-width:100%;
  min-height:20px;border:1px solid var(--border,#E4E3DE);border-radius:var(--radius-chip,2px);
  background:var(--bg-light,#FBFAF8);color:var(--text-secondary,#4E5258);
  font-family:var(--font-data,"Cascadia Mono",monospace);font-size:9.5px;font-weight:500;line-height:1;
  letter-spacing:.02em;padding:0 7px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
button.mds-prov{cursor:pointer;}
button.mds-prov:hover{border-color:var(--border-strong,#AFABA1);background:var(--bg-hover,#F0EEE9);}
button.mds-prov:focus-visible{outline:2px solid var(--focus-ring,#2F6F8F);outline-offset:2px;}
.mds-prov__dot{height:6px;width:6px;border-radius:50%;flex:0 0 auto;}
.mds-prov__dot--ready{background:var(--severity-ready-fg,#2C5C40);}
.mds-prov__dot--review{background:var(--severity-review-fg,#8C4429);}
.mds-prov__dot--action{background:var(--severity-action-fg,#68450E);}
.mds-prov__dot--blocked{background:var(--severity-blocked-fg,#7E332D);}
.mds-prov__dot--info{background:var(--severity-info-fg,#5E666F);}
.mds-prov__src{font-weight:700;text-transform:uppercase;letter-spacing:.04em;}
.mds-prov__sep{color:var(--text-muted,#5E666F);opacity:.6;}
.mds-prov__meta{color:var(--text-muted,#5E666F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "provenance-chip");
  el.textContent = css;
  document.head.appendChild(el);
}

const RANK = { ready: 0, info: 1, review: 2, action: 3, blocked: 4 };

export function ProvenanceChip({
  sourceSystem,
  sourceDocumentId,
  asOfDate,
  valuationDate,
  completeness,
  reconciliation,
  reviewedBy,
  reviewedAtUtc,
  onOpen,
  className = "",
  ...rest
}) {
  inject();
  // Chip dot shows the WORST of evidence completeness and reconciliation status.
  const sevA = normalizeSeverity(completeness);
  const sevB = normalizeSeverity(reconciliation);
  const sev = RANK[sevB] > RANK[sevA] ? sevB : sevA;

  const titleParts = [
    sourceSystem && `Source: ${sourceSystem}`,
    sourceDocumentId && `Document: ${sourceDocumentId}`,
    asOfDate && `As of: ${asOfDate}`,
    valuationDate && `Valuation: ${valuationDate}`,
    completeness && `Evidence: ${humanizeStatus(completeness)}`,
    reconciliation && `Reconciliation: ${humanizeStatus(reconciliation)}`,
    reviewedBy && `Reviewed by: ${reviewedBy}${reviewedAtUtc ? ` · ${reviewedAtUtc}` : ""}`,
  ].filter(Boolean);

  const Tag = onOpen ? "button" : "span";
  const extra = onOpen ? { type: "button", onClick: onOpen } : {};
  return (
    <Tag
      className={`mds-prov${className ? " " + className : ""}`}
      title={titleParts.join("\n")}
      {...extra}
      {...rest}
    >
      <span className={`mds-prov__dot mds-prov__dot--${sev}`} aria-hidden="true" />
      {sourceSystem ? <span className="mds-prov__src">{sourceSystem}</span> : null}
      {asOfDate ? <span className="mds-prov__sep">·</span> : null}
      {asOfDate ? <span className="mds-prov__meta">as-of {asOfDate}</span> : null}
    </Tag>
  );
}
