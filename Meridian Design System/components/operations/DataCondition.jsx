// Meridian data condition — the eleven-condition vocabulary rendered three ways:
// a chip (DataCondition), a value carrying its condition (DataConditionValue),
// and the contract list an inspector shows (DataConditionLegend). Every
// condition is text + marker; color is never the sole signal, and a blank cell
// is never used to mean anything.
import React from "react";
import { conditionMeta, formatConditioned, conditionsVocabulary } from "./conditions";
// 4.0: CONDITIONS lives here now (conditions.js is bundle-internal). Function hoisting makes this safe.
DataCondition.CONDITIONS = conditionsVocabulary;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const keys = ["confirmed", "provisional", "estimated", "stale", "overridden", "missing", "na", "pending", "exception", "reconciled", "restricted"];
  const perKey = keys.map((k) => `
.mds-cond--${k}{color:var(--cond-${k}-fg);}
.mds-cond--${k}.mds-cond--chip{border-color:var(--cond-${k}-bd);background:var(--cond-${k}-bg);}
.mds-condval--${k} .mds-condval__mark{color:var(--cond-${k}-fg);}`).join("");
  const css = `
.mds-cond{display:inline-flex;align-items:center;gap:5px;font-family:var(--font-body);font-size:11px;
  font-weight:var(--weight-medium,500);line-height:1;white-space:nowrap;}
.mds-cond--chip{border:1px solid transparent;border-radius:var(--radius-chip,2px);padding:3px 7px;min-height:20px;}
.mds-cond__glyph{font-size:9px;line-height:1;display:inline-block;width:9px;text-align:center;}
.mds-cond__detail{color:var(--text-muted);font-family:var(--font-data);font-size:10px;letter-spacing:.01em;}
.mds-condval{display:inline-flex;align-items:baseline;gap:5px;font-family:var(--font-data);
  font-variant-numeric:tabular-nums;font-size:var(--type-body,13px);color:var(--text-primary);}
.mds-condval--missing,.mds-condval--na{color:var(--text-muted);}
.mds-condval--missing{color:var(--cond-missing-fg);}
.mds-condval--restricted .mds-condval__v{color:var(--cond-restricted-fg);font-style:normal;letter-spacing:.01em;}
.mds-condval__mark{font-size:8px;line-height:1;position:relative;top:-1px;}
.mds-condval--estimated .mds-condval__v,.mds-condval--stale .mds-condval__v{color:var(--text-secondary);}
.mds-legend{display:flex;flex-direction:column;gap:0;width:100%;}
.mds-legend__row{display:flex;align-items:center;gap:8px;padding:5px 0;border-bottom:1px solid var(--border-divider);}
.mds-legend__row:last-child{border-bottom:0;}
.mds-legend__name{font-size:12px;color:var(--text-secondary);flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-legend__state{font-family:var(--font-data);font-size:11px;color:var(--text-muted);white-space:nowrap;}
${perKey}`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "data-condition");
  el.textContent = css;
  document.head.appendChild(el);
}

export function DataCondition({ condition, detail, chip = false, label, showGlyph = true, className = "", ...rest }) {
  inject();
  const m = conditionMeta(condition);
  return (
    <span
      className={`mds-cond mds-cond--${m.key}${chip ? " mds-cond--chip" : ""}${className ? " " + className : ""}`}
      title={m.note}
      {...rest}
    >
      {showGlyph ? <span className="mds-cond__glyph" aria-hidden="true">{m.glyph}</span> : null}
      <span>{label || m.label}</span>
      {detail ? <span className="mds-cond__detail">{detail}</span> : null}
    </span>
  );
}

export function DataConditionValue({ value, condition, detail, align, className = "", ...rest }) {
  inject();
  const m = conditionMeta(condition);
  const text = formatConditioned(value, m.key, detail);
  const style = align === "right" ? { justifyContent: "flex-end", width: "100%" } : undefined;
  return (
    <span
      className={`mds-condval mds-condval--${m.key}${className ? " " + className : ""}`}
      style={style}
      title={`${m.label} — ${m.note}`}
      {...rest}
    >
      <span className="mds-condval__mark" aria-hidden="true">{m.key === "confirmed" ? "" : m.glyph}</span>
      <span className="mds-condval__v">{text}</span>
      <span className="mds-visually-hidden" style={{ position: "absolute", width: 1, height: 1, overflow: "hidden", clip: "rect(0 0 0 0)" }}>{m.label}</span>
    </span>
  );
}

export function DataConditionLegend({ items = [], className = "", ...rest }) {
  inject();
  return (
    <div className={`mds-legend${className ? " " + className : ""}`} {...rest}>
      {items.map((it, i) => {
        const m = conditionMeta(it.condition);
        return (
          <div className="mds-legend__row" key={it.id || it.label || i}>
            <span className={`mds-cond mds-cond--${m.key}`} title={m.note}>
              <span className="mds-cond__glyph" aria-hidden="true">{m.glyph}</span>
            </span>
            <span className="mds-legend__name">{it.label}</span>
            <span className="mds-legend__state">{it.state || m.label}</span>
          </div>
        );
      })}
    </div>
  );
}
