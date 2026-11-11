// Meridian Checkbox + Toggle — functional only. No transitions.
// Both wrap a real hidden <input> so keyboard + AT work; Toggle is role="switch".
// Checkbox supports indeterminate (bulk-select "some of N" state).
import React from "react";
// Check/dash marks inlined (core/icons.js retired in 4.0).
const mkGlyph = (vb, w, h, paths, sw = 2) => function Glyph({ size, style, ...rest }) {
  const scale = size ? size / Math.max(w, h) : 1;
  return React.createElement("svg", { viewBox: vb, width: size ? Math.round(w * scale) : w, height: size ? Math.round(h * scale) : h,
    "aria-hidden": true, focusable: "false", style: { flex: "0 0 auto", display: "block", ...style }, ...rest },
    ...paths.map((d, i) => React.createElement("path", { key: i, d, fill: "none", stroke: "currentColor", strokeWidth: sw, strokeLinecap: "square" })));
};
const GlyphCheck = mkGlyph("0 0 12 10", 11, 9, ["M1.5 5.5l3 3L10.5 2"]);
const GlyphDash = mkGlyph("0 0 12 10", 11, 9, ["M2 5h8"]);

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-chk-wrap{display:inline-flex;align-items:center;gap:8px;cursor:pointer;user-select:none;}
.mds-chk-wrap--disabled{opacity:.5;cursor:not-allowed;}
.mds-chk-box{position:relative;width:16px;height:16px;flex-shrink:0;border:1.5px solid var(--border,#D2CDC3);
  background:var(--bg-light,#F8F7F3);display:flex;align-items:center;justify-content:center;}
.mds-chk-box:focus-within{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:var(--focus-ring-offset,1px);}
.mds-chk-box--on{background:var(--interaction-selected,#496C8F);border-color:var(--interaction-selected,#496C8F);}
.mds-chk-check{color:var(--text-on-accent,#fff);display:flex;}
.mds-chk-label{font-family:var(--font-body);font-size:13px;color:var(--text-primary,#20252B);}
.mds-chk-hint{font-family:var(--font-body);font-size:11px;color:var(--text-muted,#6B737B);margin-top:1px;}

.mds-tog-wrap{display:inline-flex;align-items:center;gap:10px;min-height:24px;cursor:pointer;user-select:none;}
.mds-tog-wrap--disabled{opacity:.5;cursor:not-allowed;}
.mds-tog-track{position:relative;width:36px;height:20px;background:var(--border,#D2CDC3);flex-shrink:0;}
.mds-tog-track:focus-within{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:var(--focus-ring-offset,1px);}
.mds-tog-track--on{background:var(--interaction-selected,#496C8F);}
.mds-tog-thumb{position:absolute;top:2px;left:2px;width:16px;height:16px;background:var(--bg-light,#F8F7F3);}
.mds-tog-track--on .mds-tog-thumb{left:18px;}
.mds-tog-label{font-family:var(--font-body);font-size:13px;color:var(--text-primary,#20252B);}
.mds-hidden-input{position:absolute;inset:0;margin:0;opacity:0;cursor:inherit;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","checkbox");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Checkbox({ checked = false, indeterminate = false, onChange, label, hint, disabled = false, ...rest }) {
  inject();
  const inputRef = React.useRef(null);
  // Native indeterminate lives on the element, not an attribute — AT then reads "mixed".
  React.useEffect(() => {
    if (inputRef.current) inputRef.current.indeterminate = !checked && indeterminate;
  }, [checked, indeterminate]);
  const mark = checked ? <GlyphCheck size={9} /> : (indeterminate ? <GlyphDash size={9} /> : null);
  return (
    <label className={`mds-chk-wrap${disabled ? " mds-chk-wrap--disabled" : ""}`}>
      <div className={`mds-chk-box${checked || indeterminate ? " mds-chk-box--on" : ""}`}>
        <input {...rest} ref={inputRef} type="checkbox" className="mds-hidden-input" checked={checked}
          onChange={e => onChange?.(e.target.checked)} disabled={disabled} />
        {mark && <span className="mds-chk-check" aria-hidden="true">{mark}</span>}
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

export function Toggle({ checked = false, onChange, label, disabled = false, ...rest }) {
  inject();
  return (
    <label className={`mds-tog-wrap${disabled ? " mds-tog-wrap--disabled" : ""}`}>
      <div className={`mds-tog-track${checked ? " mds-tog-track--on" : ""}`}>
        <input {...rest} type="checkbox" role="switch" className="mds-hidden-input" checked={checked}
          aria-checked={checked} onChange={e => onChange?.(e.target.checked)} disabled={disabled} />
        <div className="mds-tog-thumb" aria-hidden="true" />
      </div>
      {label && <span className="mds-tog-label">{label}</span>}
    </label>
  );
}
