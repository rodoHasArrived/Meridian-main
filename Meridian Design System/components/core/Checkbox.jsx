// Meridian Checkbox + Toggle — standalone form controls for boolean fields.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-chk-wrap{display:inline-flex;align-items:center;gap:8px;cursor:pointer;user-select:none;}
.mds-chk-wrap--disabled{opacity:.5;cursor:not-allowed;}
.mds-chk-box{width:16px;height:16px;flex-shrink:0;border:1.5px solid var(--border,#D7DCE2);
  border-radius:3px;background:var(--bg-light,#fff);display:flex;align-items:center;justify-content:center;
  transition:border-color .1s ease,background .1s ease;}
.mds-chk-box:focus-within{box-shadow:0 0 0 2px rgba(47,111,143,.25);}
.mds-chk-box--checked{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);}
.mds-chk-check{color:white;font-size:10px;font-weight:700;line-height:1;}
.mds-chk-label{font-family:var(--font-body);font-size:13px;color:var(--text-primary,#22272E);}
.mds-chk-hint{font-family:var(--font-body);font-size:11px;color:var(--text-muted,#6E7781);margin-top:1px;}

.mds-tog-wrap{display:inline-flex;align-items:center;gap:10px;cursor:pointer;user-select:none;}
.mds-tog-wrap--disabled{opacity:.5;cursor:not-allowed;}
.mds-tog-track{width:36px;height:20px;border-radius:10px;background:var(--border,#D7DCE2);
  position:relative;flex-shrink:0;transition:background .15s ease;}
.mds-tog-track--on{background:var(--accent,#2F6F8F);}
.mds-tog-thumb{position:absolute;top:2px;left:2px;width:16px;height:16px;
  border-radius:50%;background:white;
  box-shadow:0 1px 3px rgba(23,26,31,.20);
  transition:transform .15s ease;}
.mds-tog-track--on .mds-tog-thumb{transform:translateX(16px);}
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
    <label className={`mds-tog-wrap${disabled ? " mds-tog-wrap--disabled" : ""}`}>
      <div className={`mds-tog-track${checked ? " mds-tog-track--on" : ""}`}
        onClick={() => !disabled && onChange?.(!checked)}>
        <div className="mds-tog-thumb" />
      </div>
      {label && <span className="mds-tog-label">{label}</span>}
    </label>
  );
}
