// Meridian SegmentedControl — compact mutually-exclusive view switch (table/chart, day/week/
// month). Denser than RadioGroup, lives in toolbars. Functional, no transitions.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-seg{display:inline-flex;align-items:stretch;padding:2px;gap:2px;
  background:var(--bg-medium,#F5F7FA);border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-button,4px);font-family:var(--font-body);}
.mds-seg--full{display:flex;width:100%;}
.mds-seg__btn{flex:1;display:inline-flex;align-items:center;justify-content:center;gap:6px;
  padding:5px 12px;font-size:12px;font-weight:500;line-height:1;white-space:nowrap;cursor:pointer;
  color:var(--text-secondary,#4D5967);background:transparent;border:none;
  border-radius:var(--radius-chip,3px);}
.mds-seg__btn:hover:not(.mds-seg__btn--active):not(:disabled){color:var(--text-primary,#22272E);background:var(--bg-hover,#F1F4F7);}
.mds-seg__btn--active{color:var(--text-primary,#22272E);background:var(--bg-light,#FAFBFC);
  font-weight:600;box-shadow:var(--shadow-card,0 1px 2px rgba(0,0,0,.06));}
.mds-seg__btn:disabled{opacity:.45;cursor:not-allowed;}
.mds-seg--sm .mds-seg__btn{padding:3px 9px;font-size:11px;}
.mds-seg__count{font-family:var(--font-data,monospace);font-size:11px;
  color:var(--text-muted,#59636F);font-weight:600;}
.mds-seg__btn--active .mds-seg__count{color:var(--accent,#2F6F8F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "segmented");
  el.textContent = css;
  document.head.appendChild(el);
}

function normalize(options) {
  return (options || []).map(o =>
    typeof o === "string" ? { value: o, label: o } : o);
}

export function SegmentedControl({
  options = [], value, onChange, size = "md", fullWidth = false,
  className = "", ...rest
}) {
  inject();
  const items = normalize(options);
  return (
    <div role="tablist"
      className={`mds-seg mds-seg--${size}${fullWidth ? " mds-seg--full" : ""}${className ? " " + className : ""}`}
      {...rest}>
      {items.map((opt) => {
        const active = opt.value === value;
        return (
          <button key={opt.value} type="button" role="tab" aria-selected={active}
            disabled={opt.disabled}
            className={`mds-seg__btn${active ? " mds-seg__btn--active" : ""}`}
            onClick={() => !opt.disabled && onChange?.(opt.value)}>
            {opt.icon && <span aria-hidden="true">{opt.icon}</span>}
            <span>{opt.label}</span>
            {opt.count != null && <span className="mds-seg__count">{opt.count}</span>}
          </button>
        );
      })}
    </div>
  );
}
