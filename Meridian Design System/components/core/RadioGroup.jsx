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
.mds-radio{display:inline-flex;min-height:24px;align-items:flex-start;gap:8px;cursor:pointer;user-select:none;}
.mds-radio--disabled{opacity:.5;cursor:not-allowed;}
.mds-radio__ring{width:16px;height:16px;flex:0 0 auto;margin-top:1px;border-radius:50%;
  border:1.5px solid var(--border,#E4E3DE);background:var(--bg-light,#FBFAF8);
  display:flex;align-items:center;justify-content:center;}
.mds-radio:has(input:focus-visible) .mds-radio__ring{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-radio--checked .mds-radio__ring{border-color:var(--accent,#A85436);}
.mds-radio__dot{width:8px;height:8px;border-radius:50%;background:var(--accent,#A85436);}
.mds-radio__label{font-size:13px;color:var(--text-primary,#22252A);line-height:18px;}
.mds-radio__hint{font-size:11px;color:var(--text-muted,#5E666F);margin-top:1px;}
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
  const generatedName = React.useId();
  const groupName = name || generatedName;
  const inputs = React.useRef(new Map());
  const items = normalize(options);
  const enabled = items.filter((option) => !(disabled || option.disabled));
  const tabStop = enabled.some((option) => option.value === value) ? value : enabled[0]?.value;
  const handleKeyDown = (event, current) => {
    if (enabled.length === 0) return;
    const index = enabled.findIndex((option) => option.value === current);
    let nextIndex;
    switch (event.key) {
      case "ArrowRight": case "ArrowDown": nextIndex = (index + 1) % enabled.length; break;
      case "ArrowLeft": case "ArrowUp": nextIndex = (index - 1 + enabled.length) % enabled.length; break;
      case "Home": nextIndex = 0; break;
      case "End": nextIndex = enabled.length - 1; break;
      case " ": case "Enter": event.preventDefault(); onChange?.(current); return;
      default: return;
    }
    event.preventDefault();
    const next = enabled[nextIndex].value;
    inputs.current.get(next)?.focus();
    onChange?.(next);
  };
  return (
    <div role="radiogroup" aria-orientation={orientation} aria-disabled={disabled || undefined}
      className={`mds-radio-group${orientation === "horizontal" ? " mds-radio-group--row" : ""}${className ? " " + className : ""}`}
      {...rest}>
      {items.map((opt) => {
        const checked = opt.value === value;
        const itemDisabled = disabled || opt.disabled;
        return (
          <label key={opt.value}
            className={`mds-radio${checked ? " mds-radio--checked" : ""}${itemDisabled ? " mds-radio--disabled" : ""}`}>
            <span className="mds-radio__ring">
              <input type="radio" name={groupName} value={opt.value} checked={checked} disabled={itemDisabled}
                tabIndex={opt.value === tabStop ? 0 : -1}
                ref={(element) => {
                  if (element) inputs.current.set(opt.value, element);
                  else inputs.current.delete(opt.value);
                }}
                onKeyDown={(event) => !itemDisabled && handleKeyDown(event, opt.value)}
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
