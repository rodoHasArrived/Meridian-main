// Meridian MaterialityAssessment — materiality is not a variance threshold. A $100 difference can
// be qualitatively significant while a $10,000 timing item is operationally explainable, so the
// assessment carries its own policy, quantitative benchmark AND qualitative factor checklist, and
// renders as a named classification — never as a bare colored pill.
import React from "react";
import { formatMoney, formatNumber } from "./format.js";

const CLASS = {
  immaterial:  { label: "Immaterial",  glyph: "○", rank: 0, note: "No reporting or operational consequence" },
  operational: { label: "Operational", glyph: "◐", rank: 1, note: "Consequential for process or control, not for reporting" },
  financial:   { label: "Financial",   glyph: "◑", rank: 2, note: "Consequential for the financial statements" },
  reporting:   { label: "Reporting",   glyph: "●", rank: 3, note: "Consequential for external reporting or disclosure" },
};
const money = (v, cur) => v == null ? null : cur
  ? formatMoney(v, { currency: cur, minDecimals: 0, maxDecimals: 2 })
  : formatNumber(v, { minDecimals: 0, maxDecimals: 2 });

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return; injected = true;
  const el = document.createElement("style"); el.setAttribute("data-mds", "materiality");
  el.textContent = `
.mds-mat{display:inline-flex;align-items:baseline;gap:5px;font:11px/1.35 var(--font-body);color:var(--text-primary,#20252B);border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);padding:1px 6px;white-space:nowrap}
.mds-mat__g{font:10px var(--font-data,monospace);color:var(--text-secondary,#535C65)}
.mds-mat__q{font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-mat[data-rank="2"],.mds-mat[data-rank="3"]{border-color:var(--border-strong,#B2BAC3);font-weight:600}
button.mds-mat{cursor:pointer}
button.mds-mat:hover{background:var(--bg-hover,#EDEFF0)}
button.mds-mat:focus-visible{outline:2px solid var(--interaction-focus,var(--focus,#2D6FB3));outline-offset:1px}
.mds-matp{font-family:var(--font-body);color:var(--text-primary,#20252B);display:grid;gap:10px}
.mds-matp__hd{display:flex;align-items:baseline;justify-content:space-between;gap:12px;padding-bottom:6px;border-bottom:1px solid var(--border,#D2CDC3)}
.mds-matp__cls{font:600 13px/1.3 var(--font-body)}
.mds-matp__clsnote{display:block;font:10px/1.45 var(--font-body);color:var(--text-muted,#606870);max-width:44ch}
.mds-matp__amt{font:600 13px var(--font-data,monospace);font-variant-numeric:tabular-nums;text-align:right;white-space:nowrap}
.mds-matp__amtsub{display:block;font-weight:400;font-size:10px;color:var(--text-muted,#606870)}
.mds-matp__sec{display:grid;gap:4px}
.mds-matp__lbl{font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.06em;color:var(--text-muted,#606870)}
.mds-matp__grid{display:grid;grid-template-columns:auto 1fr;gap:2px 12px;font:11px/1.5 var(--font-body)}
.mds-matp__grid dt{color:var(--text-muted,#606870)}
.mds-matp__grid dd{margin:0;font-family:var(--font-data,monospace);font-variant-numeric:tabular-nums}
.mds-matp__f{display:grid;gap:0;margin:0;padding:0;list-style:none}
.mds-matp__fi{display:grid;grid-template-columns:14px 1fr;gap:8px;padding:4px 0;border-bottom:1px solid var(--border-divider,#E6E2DA);font:11px/1.5 var(--font-body)}
.mds-matp__fi:last-child{border-bottom:0}
.mds-matp__fi[data-present="false"]{color:var(--text-muted,#606870)}
.mds-matp__fm{font:11px var(--font-data,monospace);text-align:center}
.mds-matp__fr{display:block;font:10px/1.45 var(--font-body);color:var(--text-muted,#606870);text-wrap:pretty}
.mds-matp__pol{font:10px/1.5 var(--font-data,monospace);color:var(--text-muted,#606870);border-top:1px solid var(--border-divider,#E6E2DA);padding-top:6px}
`;
  document.head.appendChild(el);
}

const label = a => (CLASS[a && a.classification] || { label: a && a.classification || "Unassessed", glyph: "·", rank: 0, note: "" });

export function MaterialityBadge({ assessment, classification, showAmount = false, currency = "USD", onOpen, className = "", ...rest }) {
  inject();
  const a = assessment || (classification ? { classification } : null);
  const c = label(a);
  const amt = showAmount && a && a.amount != null ? money(a.amount, currency) : null;
  const pct = showAmount && a && a.percentage != null ? `${a.percentage}%` : null;
  const Tag = onOpen ? "button" : "span";
  return <Tag className={`mds-mat${className ? " " + className : ""}`} data-rank={c.rank} data-classification={a ? a.classification : undefined}
    {...(onOpen ? { type: "button", onClick: onOpen } : {})} title={`Materiality · ${c.label}${c.note ? ` — ${c.note}` : ""}`} {...rest}>
    <span className="mds-mat__g" aria-hidden="true">{c.glyph}</span>{c.label}
    {amt || pct ? <span className="mds-mat__q">{[amt, pct].filter(Boolean).join(" · ")}</span> : null}
  </Tag>;
}

export function MaterialityAssessment({ assessment, currency = "USD", title = "Materiality", className = "", ...rest }) {
  inject();
  if (!assessment) return null;
  const a = assessment, c = label(a), q = a.quantitative || {}, factors = a.qualitativeFactors || [];
  const present = factors.filter(f => f.present).length;
  return <section className={`mds-matp${className ? " " + className : ""}`} {...rest}>
    <header className="mds-matp__hd">
      <span><span className="mds-matp__lbl">{title}</span>
        <span className="mds-matp__cls">{c.glyph} {c.label}</span>
        {c.note ? <span className="mds-matp__clsnote">{c.note}</span> : null}</span>
      {a.amount != null || a.percentage != null ? <span className="mds-matp__amt">{money(a.amount, currency) || "—"}
        {a.percentage != null ? <span className="mds-matp__amtsub">{a.percentage}% of benchmark</span> : null}</span> : null}
    </header>
    {q.threshold != null || q.benchmark || q.ratio != null ? <div className="mds-matp__sec">
      <span className="mds-matp__lbl">Quantitative</span>
      <dl className="mds-matp__grid">
        {q.benchmark ? <><dt>Benchmark</dt><dd>{q.benchmark}</dd></> : null}
        {q.threshold != null ? <><dt>Threshold</dt><dd>{money(q.threshold, currency)}</dd></> : null}
        {q.ratio != null ? <><dt>Ratio</dt><dd>{(q.ratio * 100).toFixed(2)}%</dd></> : null}
      </dl></div> : null}
    {factors.length ? <div className="mds-matp__sec">
      <span className="mds-matp__lbl">Qualitative factors · {present} of {factors.length} present</span>
      <ul className="mds-matp__f">{factors.map(f => <li key={f.id} className="mds-matp__fi" data-present={f.present ? "true" : "false"}>
        <span className="mds-matp__fm" aria-hidden="true">{f.present ? "▪" : "·"}</span>
        <span>{f.label}<span className="mds-matp__fr">{f.present ? (f.rationale || "Present") : "Not present"}</span></span>
      </li>)}</ul></div> : null}
    <p className="mds-matp__pol">Policy {a.policyId}{a.policyVersion ? ` v${a.policyVersion}` : ""}
      {a.evaluatedAt ? ` · evaluated ${a.evaluatedAt}` : ""}{a.evaluatedBy ? ` · ${a.evaluatedBy}` : ""}
      <br />Materiality is assessed independently of tolerance. A within-tolerance variance can still be material.</p>
  </section>;
}
