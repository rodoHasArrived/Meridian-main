// Meridian RadioGroup — the missing single-select form primitive (Checkbox/Toggle covered
// multi/boolean). Square-ish radio with a filled center dot. Functional, no transitions.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-radio-group{display:flex;flex-direction:column;gap:10px;font-family:var(--font-body);}
.mds-radio-group--row{flex-direction:row;flex-wrap:wrap;gap:16px;}
.mds-radio{display:inline-flex;align-items:flex-start;gap:8px;cursor:pointer;user-select:none;}
.mds-radio--disabled{opacity:.5;cursor:not-allowed;}
.mds-radio__ring{width:16px;height:16px;flex:0 0 auto;margin-top:1px;border-radius:50%;
  border:1.5px solid var(--border,#D7DCE2);background:var(--bg-light,#FAFBFC);
  display:flex;align-items:center;justify-content:center;}
.mds-radio__ring:focus-within{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-radio--checked .mds-radio__ring{border-color:var(--accent,#2F6F8F);}
.mds-radio__dot{width:8px;height:8px;border-radius:50%;background:var(--accent,#2F6F8F);}
.mds-radio__label{font-size:13px;color:var(--text-primary,#22272E);line-height:18px;}
.mds-radio__hint{font-size:11px;color:var(--text-muted,#59636F);margin-top:1px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "radiogroup");
  el.textContent = css;
  document.head.appendChild(el);
}

function normalize(options) {
  return (options || []).map(o =>
    typeof o === "string" ? { value: o, label: o } : o);
}

export function RadioGroup({
  options = [], value, onChange, name, orientation = "vertical",
  disabled = false, className = "", ...rest
}) {
  inject();
  const groupName = name || React.useId?.() || "mds-radio";
  const items = normalize(options);
  return (
    <div role="radiogroup"
      className={`mds-radio-group${orientation === "horizontal" ? " mds-radio-group--row" : ""}${className ? " " + className : ""}`}
      {...rest}>
      {items.map((opt) => {
        const checked = opt.value === value;
        const itemDisabled = disabled || opt.disabled;
        return (
          <label key={opt.value}
            className={`mds-radio${checked ? " mds-radio--checked" : ""}${itemDisabled ? " mds-radio--disabled" : ""}`}>
            <span className="mds-radio__ring">
              <input type="radio" name={groupName} checked={checked} disabled={itemDisabled}
                onChange={() => onChange?.(opt.value)}
                style={{ position: "absolute", opacity: 0, width: 0, height: 0 }} />
              {checked && <span className="mds-radio__dot" />}
            </span>
            <span>
              <span className="mds-radio__label">{opt.label}</span>
              {opt.hint && <span className="mds-radio__hint" style={{ display: "block" }}>{opt.hint}</span>}
            </span>
          </label>
        );
      })}
    </div>
  );
}
