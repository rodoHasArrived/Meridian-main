// Meridian ValueAuthority — a financial workstation shows several legitimate versions of the
// same value (source, calculated, override, approved, posted, reported). Rather than making every
// surface guess which one is "current", the authority contract names the candidates, marks which
// stage is authoritative here, and says why. The language is "authoritative", never "true":
// an approved accounting amount and a later external source can both be valid in their own role.
import React from "react";
import { formatNumber } from "./format.js";
import { conditionMeta } from "../operations/conditions";

const STAGES = ["source", "calculated", "override", "approved", "posted", "reported"];
const STAGE = {
  source:     { label: "Source",     glyph: "◇", note: "As received from the external system of record" },
  calculated: { label: "Calculated", glyph: "ƒ", note: "Derived by a Meridian calculation" },
  override:   { label: "Override",   glyph: "◆", note: "Manually entered against the derived value" },
  approved:   { label: "Approved",   glyph: "✓", note: "Accepted by an authorized reviewer" },
  posted:     { label: "Posted",     glyph: "▣", note: "Written to the ledger for the period" },
  reported:   { label: "Reported",   glyph: "▲", note: "Published in an external report" },
};
const candidateCondition = candidate => candidate.condition ? conditionMeta(candidate.condition) : null;
const candidateValue = (candidate, format) => {
  const key = candidateCondition(candidate)?.key;
  return key === "restricted" ? "Restricted" : key === "missing" || key === "na" ? "—" : fmt(candidate.value, format);
};
const candidateUnit = (candidate, unit) => unit && typeof candidate.value === "number" && !["restricted", "missing", "na"].includes(candidateCondition(candidate)?.key) ? ` ${unit}` : "";

const order = s => { const i = STAGES.indexOf(s); return i < 0 ? 99 : i; };
export function resolveAuthority(authority) {
  if (!authority) return null;
  const candidates = [...(authority.candidates || [])].sort((a, b) => order(a.stage) - order(b.stage));
  const stage = authority.authoritativeStage || (candidates.length ? candidates[candidates.length - 1].stage : null);
  return { ...authority, candidates, authoritativeStage: stage, authoritative: candidates.find(c => c.stage === stage) || null };
}
const fmt = (v, f) => (f ? f(v) : typeof v === "number" ? formatNumber(v, { decimals: 2 }) : v == null ? "—" : String(v));

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return; injected = true;
  const el = document.createElement("style"); el.setAttribute("data-mds", "value-authority");
  el.textContent = `
.mds-auth{font-family:var(--font-body);color:var(--text-primary,#20252B)}
.mds-auth__hd{display:flex;align-items:baseline;justify-content:space-between;gap:12px;padding-bottom:6px;border-bottom:1px solid var(--border,#D2CDC3)}
.mds-auth__ttl{font:600 11px/1.3 var(--font-body);letter-spacing:.02em}
.mds-auth__stagelabel{font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.06em;color:var(--text-muted,#606870)}
.mds-auth__list{display:grid;gap:0;margin:0;padding:0;list-style:none}
.mds-auth__row{display:grid;grid-template-columns:14px 1fr auto;align-items:baseline;gap:8px;padding:6px 8px 6px 0;border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-auth__row:last-child{border-bottom:0}
.mds-auth__row--is{background:var(--interaction-selected-surface,var(--selected-surface,#E7EDF2));box-shadow:inset 2px 0 0 var(--interaction-selected,var(--selected,#496C8F))}
.mds-auth__row--is{padding-left:8px}
.mds-auth__row--after{color:var(--text-secondary,#535C65)}
.mds-auth__row--is .mds-auth__meta{color:var(--text-secondary,#535C65)}
.mds-auth__glyph{font:11px var(--font-data,monospace);color:var(--text-muted,#606870);text-align:center}
.mds-auth__stage{font:600 11px/1.4 var(--font-body)}
.mds-auth__meta{display:block;font:10px/1.45 var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-auth__val{font:600 12px/1.4 var(--font-data,monospace);font-variant-numeric:tabular-nums;text-align:right;white-space:nowrap}
.mds-auth__mark{display:block;font:9px var(--font-data,monospace);font-weight:600;letter-spacing:.05em;text-transform:uppercase;color:var(--text-secondary,#535C65)}
.mds-auth__why{margin:8px 0 0;font:11px/1.5 var(--font-body);color:var(--text-secondary,#535C65);max-width:52ch;text-wrap:pretty}
.mds-auth__policy{margin-top:4px;font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-authchip{display:inline-flex;align-items:baseline;gap:4px;font:10px/1.3 var(--font-data,monospace);color:var(--text-secondary,#535C65);border:1px solid var(--border,#D2CDC3);padding:1px 5px;background:var(--bg-light,#F8F7F3);white-space:nowrap}
.mds-authchip[data-authoritative="false"]{border-style:dashed;color:var(--text-muted,#606870)}
.mds-auth__tbl{width:100%;border-collapse:collapse;font-family:var(--font-body)}
.mds-auth__tbl th{font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.05em;color:var(--text-muted,#606870);text-align:left;padding:4px 8px 4px 0;border-bottom:1px solid var(--border,#D2CDC3)}
.mds-auth__tbl td{font:11px/1.5 var(--font-body);padding:5px 8px 5px 0;border-bottom:1px solid var(--border-divider,#E2DED5);vertical-align:top}
.mds-auth__tbl td.num{font-family:var(--font-data,monospace);font-variant-numeric:tabular-nums;text-align:right;font-weight:600}
.mds-auth__tbl tr[data-authoritative="true"] td{background:var(--interaction-selected-surface,var(--selected-surface,#E7EDF2))}
`;
  document.head.appendChild(el);
}

export function AuthorityChip({ stage, authoritative = false, version, className = "", ...rest }) {
  inject();
  const s = STAGE[stage] || { label: stage, glyph: "·" };
  return <span className={`mds-authchip${className ? " " + className : ""}`} data-authoritative={authoritative ? "true" : "false"}
    title={authoritative ? `${s.label} value — authoritative here` : `${s.label} value — not authoritative here`} {...rest}>
    <span aria-hidden="true">{s.glyph}</span>{s.label}{version != null ? ` v${version}` : ""}{authoritative ? " · authoritative" : ""}
  </span>;
}

export function AuthorityStack({ authority, label = "Value authority", unit, format, showMeta = true, className = "", ...rest }) {
  inject();
  const a = resolveAuthority(authority);
  if (!a) return null;
  const isIndex = a.candidates.findIndex(c => c.stage === a.authoritativeStage);
  return <section className={`mds-auth${className ? " " + className : ""}`} {...rest}>
    <header className="mds-auth__hd"><span className="mds-auth__ttl">{label}</span>
      <span className="mds-auth__stagelabel">Authoritative · {(STAGE[a.authoritativeStage] || {}).label || "—"}</span></header>
    <ul className="mds-auth__list">
      {a.candidates.map((c, i) => {
        const s = STAGE[c.stage] || { label: c.stage, glyph: "·", note: "" };
        const on = c.stage === a.authoritativeStage;
        const meta = [c.version != null ? `v${c.version}` : null, c.effectiveAt, c.sourceRef, c.actorId, c.condition ? candidateCondition(c).label : null].filter(Boolean).join(" · ");
        return <li key={c.stage + i} className={`mds-auth__row${on ? " mds-auth__row--is" : ""}${isIndex >= 0 && i > isIndex ? " mds-auth__row--after" : ""}`} data-stage={c.stage} data-authoritative={on ? "true" : undefined}>
          <span className="mds-auth__glyph" aria-hidden="true">{s.glyph}</span>
          <span><span className="mds-auth__stage">{s.label}</span>{showMeta && meta ? <span className="mds-auth__meta">{meta}</span> : null}</span>
          <span className="mds-auth__val">{candidateValue(c, format)}{candidateUnit(c, unit)}
            {on ? <span className="mds-auth__mark">Authoritative</span> : null}</span>
        </li>;
      })}
    </ul>
    {a.rationale ? <p className="mds-auth__why">{a.rationale}</p> : null}
    {a.policyRef ? <p className="mds-auth__policy">Policy {a.policyRef}</p> : null}
  </section>;
}

// Namespace export: the authority contract as one reachable object (stages, resolver, surfaces).
export const ValueAuthority = {
  stages: STAGES,
  stageMeta: STAGE,
  resolve: resolveAuthority,
  Stack: props => <AuthorityStack {...props} />,
  Chip: props => <AuthorityChip {...props} />,
  Panel: props => <AuthorityPanel {...props} />,
};

export function AuthorityPanel({ authority, unit, format, caption, className = "", ...rest }) {
  inject();
  const a = resolveAuthority(authority);
  if (!a) return null;
  return <div className={`mds-auth${className ? " " + className : ""}`} {...rest}>
    <table className="mds-auth__tbl">
      {caption ? <caption style={{ captionSide: "top", textAlign: "left", font: "600 11px var(--font-body)", paddingBottom: 6 }}>{caption}</caption> : null}
      <thead><tr><th scope="col">Stage</th><th scope="col">Version</th><th scope="col">Effective</th><th scope="col">Actor</th><th scope="col">Condition</th><th scope="col" style={{ textAlign: "right" }}>Value</th></tr></thead>
      <tbody>{a.candidates.map((c, i) => {
        const on = c.stage === a.authoritativeStage;
        return <tr key={c.stage + i} data-authoritative={on ? "true" : undefined}>
          <td>{(STAGE[c.stage] || {}).label || c.stage}{on ? <span className="mds-auth__mark">Authoritative</span> : null}</td>
          <td>{c.version != null ? `v${c.version}` : "—"}</td><td>{c.effectiveAt || "—"}</td><td>{c.actorId || "—"}</td>
          <td>{c.condition ? candidateCondition(c).label : "—"}</td>
          <td className="num">{candidateValue(c, format)}{candidateUnit(c, unit)}</td>
        </tr>;
      })}</tbody>
    </table>
    {a.rationale ? <p className="mds-auth__why">{a.rationale}</p> : null}
    {a.policyRef ? <p className="mds-auth__policy">Policy {a.policyRef}</p> : null}
  </div>;
}
