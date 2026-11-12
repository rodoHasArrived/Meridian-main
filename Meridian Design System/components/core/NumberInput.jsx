// Meridian NumberInput — numeric input with +/- buttons, functional only.
import React from "react";
const { useState } = React;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-number-field{display:block;}
.mds-number-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#5E666F);margin-bottom:5px;}
.mds-number-wrap{display:flex;align-items:center;border:1px solid var(--border,#E4E3DE);
  background:var(--bg-light,#FBFAF8);height:32px;}
.mds-number-btn{width:32px;height:32px;border:none;background:transparent;color:var(--text-primary,#22252A);
  cursor:pointer;font-size:14px;font-weight:600;display:flex;align-items:center;justify-content:center;
  border-right:1px solid var(--border,#E4E3DE);}
.mds-number-btn:last-of-type{border-right:none;border-left:1px solid var(--border,#E4E3DE);}
.mds-number-btn:hover{background:var(--bg-hover,#F0EEE9);}
.mds-number-btn:active{background:var(--bg-active,#F2E3DB);}
.mds-number-btn:disabled{opacity:.5;cursor:not-allowed;}
.mds-number-input{flex:1;border:none;background:transparent;color:var(--text-primary,#22252A);
  font-family:var(--font-data);font-size:13px;padding:0 8px;text-align:center;}
.mds-number-input:focus-visible,.mds-number-btn:focus-visible{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-number-input::placeholder{color:var(--text-disabled,#94999F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "numberinput");
  el.textContent = css;
  document.head.appendChild(el);
}

export function NumberInput({ label, value = 0, onChange, min, max, step = 1, disabled = false, id, "aria-label": ariaLabel, "aria-labelledby": ariaLabelledBy, "aria-describedby": ariaDescribedBy, "aria-invalid": ariaInvalid }) {
  inject();
  const generatedId = React.useId();
  const inputId = id || generatedId;
  const [localValue, setLocalValue] = useState(value);

  const handleChange = (newVal) => {
    if (min !== undefined && newVal < min) newVal = min;
    if (max !== undefined && newVal > max) newVal = max;
    setLocalValue(newVal);
    onChange?.(newVal);
  };

  return (
    <div>
      {label && <label htmlFor={inputId} className="mds-number-label">{label}</label>}
      <div className="mds-number-wrap">
        <button
          type="button" aria-label={`Decrease ${label || ariaLabel || "value"}`}
          className="mds-number-btn"
          onClick={() => handleChange(localValue - step)}
          disabled={disabled || (min !== undefined && localValue <= min)}
        >
          −
        </button>
        <input
          id={inputId} aria-label={ariaLabel} aria-labelledby={ariaLabelledBy} aria-describedby={ariaDescribedBy} aria-invalid={ariaInvalid}
          type="number"
          className="mds-number-input"
          value={localValue}
          onChange={(e) => handleChange(parseFloat(e.target.value) || 0)}
          min={min}
          max={max}
          step={step}
          disabled={disabled}
        />
        <button
          type="button" aria-label={`Increase ${label || ariaLabel || "value"}`}
          className="mds-number-btn"
          onClick={() => handleChange(localValue + step)}
          disabled={disabled || (max !== undefined && localValue >= max)}
        >
          +
        </button>
      </div>
    </div>
  );
}
