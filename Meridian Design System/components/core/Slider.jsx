// Meridian Slider — single-value range control. Wraps a native <input type="range"> so keyboard
// (arrows/Home/End/PgUp/PgDn) and ARIA come for free; only the visuals are overridden: a thin
// square-edged track (filled via a layered solid-color div, sized to the current value — same
// technique as ProgressBar, not a CSS gradient) and a flat rectangular thumb — institutional,
// not a rounded consumer dial. No transitions, no glow.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-slider{display:flex;flex-direction:column;gap:7px;width:100%;font-family:var(--font-body);}
.mds-slider__head{display:flex;align-items:baseline;justify-content:space-between;gap:8px;}
.mds-slider__label{font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  color:var(--text-muted,#5E666F);}
.mds-slider__value{font-family:var(--font-data,monospace);font-size:12px;font-weight:600;
  color:var(--text-primary,#22252A);letter-spacing:var(--letter-spacing-data,.01em);}
.mds-slider__track-wrap{position:relative;display:flex;align-items:center;height:16px;}
.mds-slider__track-bg{position:absolute;left:0;right:0;height:6px;
  background:var(--bg-active,#F2E3DB);border:1px solid var(--border,#E4E3DE);box-sizing:border-box;}
.mds-slider__track-fill{position:absolute;left:0;height:6px;background:var(--accent,#A85436);}
.mds-slider__input{position:relative;-webkit-appearance:none;appearance:none;width:100%;height:16px;margin:0;
  background:transparent;cursor:pointer;}
.mds-slider__input:focus-visible{outline:var(--focus-ring,2px solid #A85436);outline-offset:var(--focus-ring-offset,2px);}
.mds-slider__input::-webkit-slider-runnable-track{height:6px;background:transparent;border:none;}
.mds-slider__input::-moz-range-track{height:6px;background:transparent;border:none;}
.mds-slider__input::-webkit-slider-thumb{-webkit-appearance:none;width:12px;height:16px;margin-top:-6px;
  border-radius:0;background:var(--accent,#A85436);border:1px solid var(--accent,#A85436);cursor:pointer;}
.mds-slider__input::-moz-range-thumb{width:12px;height:16px;border-radius:0;
  background:var(--accent,#A85436);border:1px solid var(--accent,#A85436);cursor:pointer;}
.mds-slider__input:disabled{cursor:not-allowed;opacity:.5;}
.mds-slider__input:disabled::-webkit-slider-thumb{background:var(--text-disabled,#94999F);border-color:var(--text-disabled,#94999F);}
.mds-slider__input:disabled::-moz-range-thumb{background:var(--text-disabled,#94999F);border-color:var(--text-disabled,#94999F);}
.mds-slider--success .mds-slider__track-fill{background:var(--green,#3A7A56);}
.mds-slider--success .mds-slider__input::-webkit-slider-thumb{background:var(--green,#3A7A56);border-color:var(--green,#3A7A56);}
.mds-slider--success .mds-slider__input::-moz-range-thumb{background:var(--green,#3A7A56);border-color:var(--green,#3A7A56);}
.mds-slider--warning .mds-slider__track-fill{background:var(--orange,#8A5C12);}
.mds-slider--warning .mds-slider__input::-webkit-slider-thumb{background:var(--orange,#8A5C12);border-color:var(--orange,#8A5C12);}
.mds-slider--warning .mds-slider__input::-moz-range-thumb{background:var(--orange,#8A5C12);border-color:var(--orange,#8A5C12);}
.mds-slider--danger .mds-slider__track-fill{background:var(--red,#A8443C);}
.mds-slider--danger .mds-slider__input::-webkit-slider-thumb{background:var(--red,#A8443C);border-color:var(--red,#A8443C);}
.mds-slider--danger .mds-slider__input::-moz-range-thumb{background:var(--red,#A8443C);border-color:var(--red,#A8443C);}
.mds-slider__marks{display:flex;justify-content:space-between;margin-top:-3px;}
.mds-slider__mark{font-family:var(--font-data,monospace);font-size:10px;color:var(--text-muted,#5E666F);white-space:nowrap;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "slider");
  el.textContent = css;
  document.head.appendChild(el);
}

const VARIANT_COLOR = {
  accent: "var(--accent,#A85436)",
  success: "var(--green,#3A7A56)",
  warning: "var(--orange,#8A5C12)",
  danger: "var(--red,#A8443C)",
};

export function Slider({
  label, value = 0, onChange, min = 0, max = 100, step = 1,
  showValue = false, valueFmt, marks, variant = "accent", disabled = false,
  className = "", ...rest
}) {
  inject();
  const clamped = Math.min(max, Math.max(min, value));
  const pct = max > min ? ((clamped - min) / (max - min)) * 100 : 0;
  const display = valueFmt ? valueFmt(clamped) : String(clamped);
  return (
    <div className={`mds-slider${variant !== "accent" ? ` mds-slider--${variant}` : ""}${className ? " " + className : ""}`}>
      {(label || showValue) && (
        <div className="mds-slider__head">
          {label && <span className="mds-slider__label">{label}</span>}
          {showValue && <span className="mds-slider__value">{display}</span>}
        </div>
      )}
      <div className="mds-slider__track-wrap">
        <div className="mds-slider__track-bg" />
        <div className="mds-slider__track-fill" style={{ width: pct + "%" }} />
        <input
          type="range"
          className="mds-slider__input"
          min={min}
          max={max}
          step={step}
          value={clamped}
          disabled={disabled}
          onChange={(e) => onChange?.(parseFloat(e.target.value))}
          aria-label={!label ? "Slider" : undefined}
          {...rest}
        />
      </div>
      {marks && marks.length > 0 && (
        <div className="mds-slider__marks" aria-hidden="true">
          {marks.map((m, i) => (
            <span key={i} className="mds-slider__mark">{m.label ?? m.value}</span>
          ))}
        </div>
      )}
    </div>
  );
}
