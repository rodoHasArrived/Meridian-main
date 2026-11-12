// Meridian Slider — single-value range control. Wraps a native <input type="range"> so keyboard
// (arrows/Home/End/PgUp/PgDn) and ARIA come for free; only the visuals are overridden: a thin
// square-edged track (filled via a layered solid-color div, sized to the current value — same
// technique as ProgressBar, not a CSS gradient) and a flat rectangular thumb — institutional,
// not a rounded consumer dial. No transitions, no glow.
// A11y (3.5): the visible label is programmatically associated (aria-labelledby), and a
// formatted value ships as aria-valuetext so "0.35Δ" is announced, not "0.35".
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-slider{display:flex;flex-direction:column;gap:7px;width:100%;font-family:var(--font-body);}
.mds-slider__head{display:flex;align-items:baseline;justify-content:space-between;gap:8px;}
.mds-slider__label{font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  color:var(--text-muted,#606870);}
.mds-slider__value{font-family:var(--font-data,monospace);font-size:12px;font-weight:600;
  color:var(--text-primary,#20252B);letter-spacing:var(--letter-spacing-data,.01em);}
.mds-slider__track-wrap{position:relative;display:flex;align-items:center;height:16px;}
.mds-slider__track-bg{position:absolute;left:0;right:0;height:6px;
  background:var(--bg-active,#E7EDF2);border:1px solid var(--border,#D2CDC3);box-sizing:border-box;}
.mds-slider__track-fill{position:absolute;left:0;height:6px;background:var(--brand-signal,#C56343);}
.mds-slider__input{position:relative;-webkit-appearance:none;appearance:none;width:100%;height:16px;margin:0;
  background:transparent;cursor:pointer;}
.mds-slider__input:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:var(--focus-ring-offset,2px);}
.mds-slider__input::-webkit-slider-runnable-track{height:6px;background:transparent;border:none;}
.mds-slider__input::-moz-range-track{height:6px;background:transparent;border:none;}
.mds-slider__input::-webkit-slider-thumb{-webkit-appearance:none;width:12px;height:16px;margin-top:-6px;
  border-radius:0;background:var(--brand-signal,#C56343);border:1px solid var(--brand-signal,#C56343);cursor:pointer;}
.mds-slider__input::-moz-range-thumb{width:12px;height:16px;border-radius:0;
  background:var(--brand-signal,#C56343);border:1px solid var(--brand-signal,#C56343);cursor:pointer;}
.mds-slider__input:disabled{cursor:not-allowed;opacity:.5;}
.mds-slider__input:disabled::-webkit-slider-thumb{background:var(--text-disabled,#A4ABB2);border-color:var(--text-disabled,#A4ABB2);}
.mds-slider__input:disabled::-moz-range-thumb{background:var(--text-disabled,#A4ABB2);border-color:var(--text-disabled,#A4ABB2);}
.mds-slider--success .mds-slider__track-fill{background:var(--green,#2E7655);}
.mds-slider--success .mds-slider__input::-webkit-slider-thumb{background:var(--green,#2E7655);border-color:var(--green,#2E7655);}
.mds-slider--success .mds-slider__input::-moz-range-thumb{background:var(--green,#2E7655);border-color:var(--green,#2E7655);}
.mds-slider--warning .mds-slider__track-fill{background:var(--orange,#986717);}
.mds-slider--warning .mds-slider__input::-webkit-slider-thumb{background:var(--orange,#986717);border-color:var(--orange,#986717);}
.mds-slider--warning .mds-slider__input::-moz-range-thumb{background:var(--orange,#986717);border-color:var(--orange,#986717);}
.mds-slider--danger .mds-slider__track-fill{background:var(--red,#B54B42);}
.mds-slider--danger .mds-slider__input::-webkit-slider-thumb{background:var(--red,#B54B42);border-color:var(--red,#B54B42);}
.mds-slider--danger .mds-slider__input::-moz-range-thumb{background:var(--red,#B54B42);border-color:var(--red,#B54B42);}
.mds-slider__marks{display:flex;justify-content:space-between;margin-top:-3px;}
.mds-slider__mark{font-family:var(--font-data,monospace);font-size:10px;color:var(--text-muted,#606870);white-space:nowrap;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "slider");
  el.textContent = css;
  document.head.appendChild(el);
}

const VARIANT_COLOR = {
  accent: "var(--brand-signal,#C56343)",
  success: "var(--green,#2E7655)",
  warning: "var(--orange,#986717)",
  danger: "var(--red,#B54B42)",
};

export function Slider({
  label, value = 0, onChange, min = 0, max = 100, step = 1,
  showValue = false, valueFmt, marks, variant = "accent", disabled = false,
  className = "", ...rest
}) {
  inject();
  const uid = React.useId();
  const labelId = label ? uid + "-label" : undefined;
  const clamped = Math.min(max, Math.max(min, value));
  const pct = max > min ? ((clamped - min) / (max - min)) * 100 : 0;
  const display = valueFmt ? valueFmt(clamped) : String(clamped);
  return (
    <div className={`mds-slider${variant !== "accent" ? ` mds-slider--${variant}` : ""}${className ? " " + className : ""}`}>
      {(label || showValue) && (
        <div className="mds-slider__head">
          {label && <span className="mds-slider__label" id={labelId}>{label}</span>}
          {showValue && <span className="mds-slider__value" aria-hidden="true">{display}</span>}
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
          aria-labelledby={labelId}
          aria-label={!label && !rest["aria-label"] ? "Slider" : rest["aria-label"]}
          aria-valuetext={valueFmt ? display : undefined}
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
