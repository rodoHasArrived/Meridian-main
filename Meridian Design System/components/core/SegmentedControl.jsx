// Meridian SegmentedControl — compact mutually-exclusive view switch (table/chart, day/week/
// month). Denser than RadioGroup, lives in toolbars. Functional, no transitions.
// A11y (3.5): a real radio group — role="radiogroup"/"radio", aria-checked, roving tabindex
// (one tab stop), Arrow/Home/End move AND select, exactly like native radios.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-seg{display:inline-flex;align-items:stretch;padding:2px;gap:2px;
  background:var(--bg-medium,#EBE8E1);border:1px solid var(--border,#D2CDC3);
  border-radius:var(--radius-button,4px);font-family:var(--font-body);}
.mds-seg--full{display:flex;width:100%;}
.mds-seg__btn{flex:1;display:inline-flex;align-items:center;justify-content:center;gap:6px;min-height:24px;
  padding:5px 12px;font-size:12px;font-weight:500;line-height:1;white-space:nowrap;cursor:pointer;
  color:var(--text-secondary,#535C65);background:transparent;border:none;
  border-radius:var(--radius-chip,3px);}
.mds-seg__btn:hover:not(.mds-seg__btn--active):not(:disabled){color:var(--text-primary,#20252B);background:var(--bg-hover,#EDEFF0);}
.mds-seg__btn:focus-visible{outline:2px solid var(--interaction-focus,#2D6FB3);outline-offset:-1px;}
.mds-seg__btn--active{color:var(--text-primary,#20252B);background:var(--bg-light,#F8F7F3);
  font-weight:600;box-shadow:var(--shadow-card,0 1px 2px rgba(0,0,0,.06));}
.mds-seg__btn:disabled{opacity:.45;cursor:not-allowed;}
.mds-seg--sm .mds-seg__btn{padding:3px 9px;font-size:11px;min-height:24px;}
.mds-seg__count{font-family:var(--font-data,monospace);font-size:11px;
  color:var(--text-muted,#6B737B);font-weight:600;}
.mds-seg__btn--active .mds-seg__count{color:var(--interaction-selected,#496C8F);}
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
  const refs = React.useRef([]);
  const enabled = items.map((o, i) => ({ o, i })).filter((x) => !x.o.disabled);
  let rovingIdx = items.findIndex((o) => o.value === value && !o.disabled);
  if (rovingIdx < 0) rovingIdx = enabled.length ? enabled[0].i : 0;
  const select = (i) => { const btn = refs.current[i]; if (btn) btn.focus(); if (!items[i].disabled) onChange?.(items[i].value); };
  const move = (from, dir) => {
    if (!enabled.length) return;
    const pos = enabled.findIndex((x) => x.i === from);
    select(enabled[((pos < 0 ? 0 : pos) + dir + enabled.length) % enabled.length].i);
  };
  const onKeyDown = (i) => (e) => {
    if (e.key === "ArrowRight" || e.key === "ArrowDown") { e.preventDefault(); move(i, 1); }
    else if (e.key === "ArrowLeft" || e.key === "ArrowUp") { e.preventDefault(); move(i, -1); }
    else if (e.key === "Home") { e.preventDefault(); if (enabled.length) select(enabled[0].i); }
    else if (e.key === "End") { e.preventDefault(); if (enabled.length) select(enabled[enabled.length - 1].i); }
  };
  return (
    <div role="radiogroup"
      className={`mds-seg mds-seg--${size}${fullWidth ? " mds-seg--full" : ""}${className ? " " + className : ""}`}
      {...rest}>
      {items.map((opt, i) => {
        const active = opt.value === value;
        return (
          <button key={opt.value} type="button" role="radio" aria-checked={active}
            ref={(el) => { refs.current[i] = el; }}
            tabIndex={i === rovingIdx ? 0 : -1}
            disabled={opt.disabled}
            onKeyDown={onKeyDown(i)}
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
