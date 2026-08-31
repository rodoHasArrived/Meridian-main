// Meridian Checkbox + Toggle — functional only. No transitions.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-chk-wrap{display:inline-flex;align-items:center;gap:8px;cursor:pointer;user-select:none;}
.mds-chk-wrap--disabled{opacity:.5;cursor:not-allowed;}
.mds-chk-box{width:16px;height:16px;flex-shrink:0;border:1.5px solid var(--border,#D7DCE2);
  background:var(--bg-light,#fff);display:flex;align-items:center;justify-content:center;}
.mds-chk-box:focus-within{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-chk-box--checked{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);}
.mds-chk-check{color:var(--text-on-accent,#fff);font-size:10px;font-weight:700;line-height:1;}
.mds-chk-label{font-family:var(--font-body);font-size:13px;color:var(--text-primary,#22272E);}
.mds-chk-hint{font-family:var(--font-body);font-size:11px;color:var(--text-muted,#59636F);margin-top:1px;}

.mds-tog-wrap{display:inline-flex;align-items:center;gap:10px;cursor:pointer;user-select:none;}
.mds-tog-wrap--disabled{opacity:.5;cursor:not-allowed;}
.mds-tog-track{width:36px;height:20px;background:var(--border,#D7DCE2);position:relative;flex-shrink:0;}
.mds-tog-track--on{background:var(--accent,#2F6F8F);}
.mds-tog-thumb{position:absolute;top:2px;left:2px;width:16px;height:16px;background:white;}
.mds-tog-track--on .mds-tog-thumb{left:18px;}
.mds-tog-label{font-family:var(--font-body);font-size:13px;color:var(--text-primary,#22272E);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","checkbox");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Checkbox({ checked = false, onChange, label, hint, disabled = false }) {
  inject();
  return (
    <label className={`mds-chk-wrap${disabled ? " mds-chk-wrap--disabled" : ""}`}>
      <div className={`mds-chk-box${checked ? " mds-chk-box--checked" : ""}`}>
        <input type="checkbox" checked={checked} onChange={e => onChange?.(e.target.checked)}
          disabled={disabled} style={{ position:"absolute", opacity:0, width:0, height:0 }} />
        {checked && <span className="mds-chk-check">✓</span>}
      </div>
      {(label || hint) && (
        <div>
          {label && <div className="mds-chk-label">{label}</div>}
          {hint  && <div className="mds-chk-hint">{hint}</div>}
        </div>
      )}
    </label>
  );
}

export function Toggle({ checked = false, onChange, label, disabled = false }) {
  inject();
  return (
    <label className={`mds-tog-wrap${disabled ? " mds-tog-wrap--disabled" : ""}`}
      onClick={() => !disabled && onChange?.(!checked)}>
      <div className={`mds-tog-track${checked ? " mds-tog-track--on" : ""}`}>
        <div className="mds-tog-thumb" />
      </div>
      {label && <span className="mds-tog-label">{label}</span>}
    </label>
  );
}
