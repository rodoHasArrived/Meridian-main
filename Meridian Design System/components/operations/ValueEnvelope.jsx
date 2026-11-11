// Meridian value envelope — ONE contract for everything a figure is known by:
// its condition, the authority that set it, the evidence behind it, the clocks it
// is stated in, and whether it is material. Replaces the practice of stacking
// DataCondition + ProvenanceChip + ValueAuthority + FreshnessIndicator +
// MaterialityBadge + SlaChip beside one number: those components remain, but a
// screen states an envelope once and the marks are derived from it.
// Three disclosure tiers: inline marks -> hover envelope -> EnvelopePanel.
import React from "react";
import { conditionMeta, formatConditioned } from "./conditions";

export const ENVELOPE_MARKS = {
  condition: { glyph: "", label: "Condition" },
  authority: { glyph: "\u25C8", label: "Authority" },
  evidence:  { glyph: "\u229E", label: "Evidence" },
  clock:     { glyph: "\u25F7", label: "Clock" },
  material:  { glyph: "\u2016", label: "Materiality" },
};

export const AUTHORITIES = {
  source:    { label: "Source of record", note: "Value as supplied by the system of record" },
  derived:   { label: "Derived", note: "Computed by a named calculation" },
  desk:      { label: "Desk mark", note: "Set by an operator with authority to mark" },
  override:  { label: "Override", note: "Operator value in force over the source" },
  estimate:  { label: "Estimate", note: "Model output pending observation" },
  unattributed: { label: "Unattributed", note: "No authority recorded — treat as unproven" },
};

const TIME_LABELS = {
  trade: "Trade", settlement: "Settlement", effective: "Effective", received: "Received",
  posting: "Posting", valuation: "Valuation", reportingPeriod: "Reporting period",
  fiscalPeriod: "Fiscal period", closeDeadline: "Close deadline", asOf: "As of",
  evidenceAt: "Evidence", calculatedAt: "Calculated",
};

const t = (v) => (v ? new Date(String(v).replace(" ", "T").replace(/Z?$/, "Z")).getTime() : NaN);

/** Coherence rules — a temporally or evidentially impossible record. */
export function envelopeCheck(env = {}) {
  const c = env.clocks || {};
  const out = [];
  if (!Number.isNaN(t(c.trade)) && !Number.isNaN(t(c.settlement)) && t(c.settlement) < t(c.trade))
    out.push({ rule: "settlement-before-trade", text: "Settles before it trades" });
  if (!Number.isNaN(t(c.evidenceAt)) && !Number.isNaN(t(c.calculatedAt)) && t(c.calculatedAt) < t(c.evidenceAt))
    out.push({ rule: "calculated-before-evidence", text: "Calculated before its evidence existed" });
  const p = c.reportingPeriod;
  if (p && p.start && p.end && !Number.isNaN(t(c.posting)) && (t(c.posting) < t(p.start) || t(c.posting) > t(p.end)))
    out.push({ rule: "posting-outside-period", text: "Posted outside its reporting period" });
  if (!Number.isNaN(t(c.closeDeadline)) && !Number.isNaN(t(c.evidenceAt)) && t(c.evidenceAt) > t(c.closeDeadline))
    out.push({ rule: "evidence-after-close", text: "Evidence arrived after the close deadline" });
  if (env.condition && conditionMeta(env.condition).key === "overridden" && !(env.authority && env.authority !== "source"))
    out.push({ rule: "override-unattributed", text: "Overridden with no authority recorded" });
  return out;
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-env{position:relative;display:inline-flex;align-items:baseline;gap:5px;font-family:var(--font-data,ui-monospace);
  font-variant-numeric:tabular-nums;font-size:var(--type-body,13px);color:var(--text-primary,#20252B);cursor:default;}
.mds-env--right{justify-content:flex-end;width:100%;}
.mds-env__v{white-space:nowrap;}
.mds-env--soft .mds-env__v{color:var(--text-secondary,#535C65);}
.mds-env__marks{display:inline-flex;align-items:center;gap:3px;font-size:8px;line-height:1;position:relative;top:-1px;}
.mds-env__mark{color:var(--text-muted,#606870);}
.mds-env__mark--bad{color:var(--red, #B54B42);}
.mds-env__pop{display:none;position:absolute;z-index:40;top:calc(100% + 4px);right:0;min-width:232px;
  background:var(--bg-surface, #F8F7F3);border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-overlay,6px);
  box-shadow:0 2px 6px rgba(20,24,28,.18);padding:8px 10px;text-align:left;}
.mds-env:hover .mds-env__pop,.mds-env:focus-within .mds-env__pop{display:block;}
.mds-env__row{display:flex;gap:10px;justify-content:space-between;align-items:baseline;padding:2px 0;
  font-family:var(--font-body,system-ui);font-size:11px;color:var(--text-secondary,#535C65);white-space:nowrap;}
.mds-env__row b{font-weight:var(--weight-medium,500);color:var(--text-primary,#20252B);font-family:var(--font-data,ui-monospace);}
.mds-env__k{font-size:10px;font-variant:all-small-caps;letter-spacing:.05em;color:var(--text-muted,#606870);}
.mds-env__viol{margin-top:5px;padding-top:5px;border-top:1px solid var(--border-divider,#E2DED5);
  font-family:var(--font-body,system-ui);font-size:11px;color:var(--red, #B54B42);}
.mds-envp{display:flex;flex-direction:column;background:var(--bg-light,#F8F7F3);border:1px solid var(--border,#D2CDC3);border-radius:2px;}
.mds-envp__hd{display:flex;justify-content:space-between;align-items:baseline;gap:12px;padding:8px 10px;
  border-bottom:1px solid var(--border-strong,#AAA49A);}
.mds-envp__title{font-family:var(--font-body,system-ui);font-size:12px;color:var(--text-secondary,#535C65);}
.mds-envp__val{font-family:var(--font-data,ui-monospace);font-variant-numeric:tabular-nums;font-size:16px;color:var(--text-primary,#20252B);}
.mds-envp__grp{padding:6px 10px;border-bottom:1px solid var(--border-divider,#E2DED5);}
.mds-envp__grp:last-child{border-bottom:0;}
.mds-envp__lb{font-size:10px;font-variant:all-small-caps;letter-spacing:.06em;color:var(--text-muted,#606870);
  font-family:var(--font-body,system-ui);margin-bottom:3px;}
.mds-envp__r{display:flex;justify-content:space-between;gap:12px;padding:2px 0;font-size:11px;
  font-family:var(--font-body,system-ui);color:var(--text-secondary,#535C65);}
.mds-envp__r span:last-child{font-family:var(--font-data,ui-monospace);color:var(--text-primary,#20252B);}
.mds-envp__bad{display:flex;gap:6px;padding:3px 0;font-size:11px;font-family:var(--font-body,system-ui);color:var(--red, #B54B42);}
.mds-envp__ev{display:flex;justify-content:space-between;gap:12px;padding:2px 0;font-size:11px;font-family:var(--font-body,system-ui);}
.mds-envp__ev a{color:var(--link, #2A5F96);text-decoration:none;border-bottom:1px solid transparent;}
.mds-envp__ev a:hover{color:var(--link-hover, #1F4A76);border-bottom-color:var(--link-hover, #1F4A76);}`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "value-envelope");
  el.textContent = css;
  document.head.appendChild(el);
}

export function EnvelopeMark({ kind, glyph, bad = false, title }) {
  inject();
  const g = glyph || (ENVELOPE_MARKS[kind] || {}).glyph || "\u00B7";
  return <span className={"mds-env__mark" + (bad ? " mds-env__mark--bad" : "")} title={title} aria-hidden="true">{g}</span>;
}

function clockRows(clocks = {}) {
  return Object.keys(TIME_LABELS)
    .filter((k) => clocks[k])
    .map((k) => {
      const v = clocks[k];
      const text = v && v.start ? `${v.start} \u2192 ${v.end}` : String(v);
      return { key: k, label: TIME_LABELS[k], text };
    });
}

/** Tier 1 + 2: the value, its derived marks, and the hover envelope. */
export function ValueEnvelope({
  value, condition = "confirmed", detail, authority, evidence = [], clocks = {},
  materiality, label, align, onInspect, className = "", ...rest
}) {
  inject();
  const m = conditionMeta(condition);
  const env = { value, condition, authority, evidence, clocks, materiality };
  const viol = envelopeCheck(env);
  const soft = m.key === "estimated" || m.key === "stale";
  const auth = AUTHORITIES[authority] || (authority ? AUTHORITIES.unattributed : null);
  const clock = clocks.asOf || clocks.valuation || clocks.trade;
  return (
    <span
      className={`mds-env${align === "right" ? " mds-env--right" : ""}${soft ? " mds-env--soft" : ""}${className ? " " + className : ""}`}
      tabIndex={0}
      onClick={onInspect}
      {...rest}
    >
      <span className="mds-env__v">{formatConditioned(value, m.key, detail)}</span>
      <span className="mds-env__marks">
        {m.key === "confirmed" ? null : <EnvelopeMark glyph={m.glyph} bad={m.key === "exception" || m.key === "missing"} title={m.label} />}
        {auth ? <EnvelopeMark kind="authority" bad={!!viol.find((v) => v.rule === "override-unattributed")} title={auth.label} /> : null}
        {evidence.length ? <EnvelopeMark kind="evidence" title={`${evidence.length} evidence item${evidence.length > 1 ? "s" : ""}`} /> : null}
        {clock ? <EnvelopeMark kind="clock" title={`As of ${clock}`} /> : null}
        {materiality && materiality.material ? <EnvelopeMark kind="material" title="Material" /> : null}
        {viol.length ? <EnvelopeMark glyph="▲" bad title={viol[0].text} /> : null}
      </span>
      <span className="mds-env__pop" role="tooltip">
        {label ? <div className="mds-env__row"><span className="mds-env__k">Figure</span><b>{label}</b></div> : null}
        <div className="mds-env__row"><span className="mds-env__k">Condition</span><b>{m.label}{detail ? " \u00B7 " + detail : ""}</b></div>
        {auth ? <div className="mds-env__row"><span className="mds-env__k">Authority</span><b>{auth.label}</b></div> : null}
        {clockRows(clocks).slice(0, 4).map((r) => (
          <div className="mds-env__row" key={r.key}><span className="mds-env__k">{r.label}</span><b>{r.text}</b></div>
        ))}
        {evidence.length ? <div className="mds-env__row"><span className="mds-env__k">Evidence</span><b>{evidence.length} linked</b></div> : null}
        {materiality ? <div className="mds-env__row"><span className="mds-env__k">Materiality</span><b>{materiality.material ? "Material" : "Below threshold"}{materiality.threshold ? " \u00B7 " + materiality.threshold : ""}</b></div> : null}
        {viol.map((v) => <div className="mds-env__viol" key={v.rule}>▲ {v.text}</div>)}
      </span>
    </span>
  );
}

/** Tier 3: the full envelope, for an inspector rail or drawer. */
export function EnvelopePanel({ envelope = {}, title, className = "", ...rest }) {
  inject();
  const m = conditionMeta(envelope.condition);
  const auth = AUTHORITIES[envelope.authority] || (envelope.authority ? AUTHORITIES.unattributed : null);
  const viol = envelopeCheck(envelope);
  const clocks = clockRows(envelope.clocks);
  const ev = envelope.evidence || [];
  return (
    <div className={`mds-envp${className ? " " + className : ""}`} {...rest}>
      <div className="mds-envp__hd">
        <span className="mds-envp__title">{title || envelope.label || "Value envelope"}</span>
        <span className="mds-envp__val">{formatConditioned(envelope.value, m.key, envelope.detail)}</span>
      </div>
      <div className="mds-envp__grp">
        <div className="mds-envp__lb">What is known</div>
        <div className="mds-envp__r"><span>Condition</span><span>{m.glyph} {m.label}</span></div>
        <div className="mds-envp__r"><span>Authority</span><span>{auth ? auth.label : "Unattributed"}</span></div>
        {envelope.materiality ? (
          <div className="mds-envp__r"><span>Materiality</span><span>{envelope.materiality.material ? "Material" : "Below threshold"}{envelope.materiality.threshold ? " \u00B7 " + envelope.materiality.threshold : ""}</span></div>
        ) : null}
      </div>
      {clocks.length ? (
        <div className="mds-envp__grp">
          <div className="mds-envp__lb">Clocks stated</div>
          {clocks.map((r) => <div className="mds-envp__r" key={r.key}><span>{r.label}</span><span>{r.text}</span></div>)}
        </div>
      ) : null}
      {ev.length ? (
        <div className="mds-envp__grp">
          <div className="mds-envp__lb">Evidence</div>
          {ev.map((e, i) => (
            <div className="mds-envp__ev" key={e.id || i}>
              <span style={{ color: "var(--text-secondary,#535C65)" }}>{e.label}</span>
              {e.href ? <a href={e.href}>{e.ref || "open"}</a> : <span style={{ fontFamily: "var(--font-data,ui-monospace)" }}>{e.ref}</span>}
            </div>
          ))}
        </div>
      ) : null}
      {viol.length ? (
        <div className="mds-envp__grp">
          <div className="mds-envp__lb">Coherence</div>
          {viol.map((v) => <div className="mds-envp__bad" key={v.rule}><span aria-hidden="true">▲</span><span>{v.text}</span></div>)}
        </div>
      ) : null}
    </div>
  );
}

/** Screen-level roll-up: every envelope on the surface that fails a rule. */
export function EnvelopeCoherence({ envelopes = [], className = "", ...rest }) {
  inject();
  const rows = [];
  envelopes.forEach((e, i) => envelopeCheck(e).forEach((v) => rows.push({ ...v, label: e.label || `Figure ${i + 1}` })));
  return (
    <div className={`mds-envp${className ? " " + className : ""}`} {...rest}>
      <div className="mds-envp__hd">
        <span className="mds-envp__title">Envelope coherence</span>
        <span className="mds-envp__val">{rows.length ? `${rows.length} failing` : "All coherent"}</span>
      </div>
      <div className="mds-envp__grp">
        {rows.length
          ? rows.map((r, i) => <div className="mds-envp__bad" key={i}><span aria-hidden="true">▲</span><span>{r.label} — {r.text}</span></div>)
          : <div className="mds-envp__r"><span>{envelopes.length} figures checked</span><span>✓</span></div>}
      </div>
    </div>
  );
}

// The bundler exposes only capitalized exports, so the helpers hang off their carrier.
ValueEnvelope.check = envelopeCheck;
EnvelopePanel.check = envelopeCheck;
